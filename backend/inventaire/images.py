"""Proxy de photos : HTTPS chez Open Food Facts, BMP non compresse pour CE.

Trois raisons de ne pas laisser le terminal chercher l'image lui-meme :

* elle est servie en HTTPS, que la pile de Windows CE 5.0 ne negocie plus ;
* elle fait 200 px de cote en JPEG, quand l'ecran lui en reserve environ 80 ;
* le decodage JPEG du Compact Framework depend de codecs qui peuvent tout
  simplement manquer de l'image OS du terminal -- un `Bitmap(flux)` renvoie
  alors une exception, pas une image degradee.

On envoie donc un BMP sans compression, a la taille exacte du cadre : un
format que le terminal decode sans rien de plus et, au pire, qu'il peut lire
octet par octet lui-meme.

**8 bits par defaut, pas 24.** Un BMP palettise pese le tiers du meme cadre en
couleurs vraies -- 41 ko contre 120 ko pour la grande photo de 200 pixels --
pour une perte invisible sur un ecran de terminal. Sur une radio 802.11b de
2005, ce tiers est la difference entre une demi-seconde d'attente et un
affichage immediat. Le format `bmp` 24 bits reste disponible pour un client
qui le demande explicitement.

Trois etages de cache : l'original tel que recu, chaque taille demandee sur le
disque, et les vignettes recentes en memoire. Rebipper un produit n'appelle
donc plus rien.

Reste le premier scan d'un produit inconnu, ou la photo doit etre telechargee
chez Open Food Facts puis convertie -- une a trois secondes pendant lesquelles
le terminal attend. `prechauffer()` fait ce travail des que la fiche est
resolue, en tache de fond : quand l'ecran demande enfin l'image, elle est deja
en memoire.
"""

from __future__ import annotations

import collections
import concurrent.futures
import hashlib
import io
import logging
import os
import threading
import urllib.error
import urllib.request
from pathlib import Path

__all__ = ["Vignettes", "PILLOW_DISPONIBLE"]

log = logging.getLogger("inventaire.images")

try:
    from PIL import Image
    PILLOW_DISPONIBLE = True
except ImportError:                                  # pragma: no cover
    Image = None
    PILLOW_DISPONIBLE = False

FORMATS = {
    "bmp8": ("image/bmp", "bmp8"),      # palettise : le tiers des octets
    "bmp": ("image/bmp", "bmp"),        # 24 bits, pour un client qui l'exige
    "jpg": ("image/jpeg", "jpg"),
    "png": ("image/png", "png"),
}
FORMAT_PAR_DEFAUT = "bmp8"

TAILLE_MAX_SOURCE = 4 * 1024 * 1024                  # garde-fou sur le telechargement
MEMOIRE_PAR_DEFAUT = 24 * 1024 * 1024               # cache en RAM des vignettes
OUVRIERS = 2                                        # fils de prechauffage

# Le delai des photos n'est pas celui de l'API. Une fiche produit fait deux
# kilo-octets de JSON et doit revenir vite, sinon le terminal reste bloque sur
# son bip ; une photo en fait dix a cent, et son telechargement se fait
# desormais en tache de fond, ou attendre ne coute rien a personne. Les serveurs
# d'images d'Open Food Facts depassent regulierement les huit secondes :
# partager le delai de l'API revenait a jeter une photo sur deux.
DELAI_PHOTO = 20.0

# Les trois cadres que le terminal dessine : la vignette des listes, celle de
# la fiche article (reglable de 48 a 96 px, 80 par defaut) et la photo plein
# ecran. Prechauffer les trois couvre tout ce qu'un scan peut declencher
# ensuite -- et comme la source n'est telechargee qu'une fois, les tailles
# supplementaires ne coutent que quelques millisecondes de redimensionnement.
TAILLES_TERMINAL = (26, 80, 200)


def _provisoire(cible: Path) -> Path:
    """Nom de fichier temporaire propre a l'ecrivain.

    Un `.part` derive de la seule cible suffisait tant qu'un seul fil ecrivait.
    Depuis le prechauffage, deux fils peuvent fabriquer la meme vignette dans la
    meme seconde -- et trois processus partagent ce cache : le serveur, le site
    et le serveur MCP. Deux `replace()` sur le meme provisoire, et le second
    echoue sur un fichier que le premier vient de renommer. Le PID et
    l'identifiant de fil rendent la collision impossible ; l'ecriture reste
    atomique, et le dernier arrive gagne avec des octets identiques.
    """
    return cible.with_suffix(f"{cible.suffix}.{os.getpid()}-{threading.get_ident()}.part")


class Memoire:
    """Cache en memoire des vignettes deja converties, borne en octets.

    Le cache disque evitait deja de retelecharger et de reconvertir. Celui-ci
    evite la lecture disque elle-meme, qui compte des lors que l'ecran de
    liste demande sept vignettes d'un coup a chaque affichage.

    Eviction du plus ancien consulte : un frigo tourne autour d'une trentaine
    de references, elles tiennent toutes en memoire et n'en sortent jamais.
    """

    def __init__(self, octets_max: int = MEMOIRE_PAR_DEFAUT) -> None:
        self.octets_max = octets_max
        self._entrees: collections.OrderedDict = collections.OrderedDict()
        self._octets = 0
        self._verrou = threading.Lock()
        self.touches = 0
        self.manques = 0

    def lire(self, cle: str):
        with self._verrou:
            valeur = self._entrees.get(cle)
            if valeur is None:
                self.manques += 1
                return None
            self._entrees.move_to_end(cle)
            self.touches += 1
            return valeur

    def ecrire(self, cle: str, corps: bytes, mime: str) -> None:
        if len(corps) > self.octets_max:
            return
        with self._verrou:
            ancienne = self._entrees.pop(cle, None)
            if ancienne is not None:
                self._octets -= len(ancienne[0])
            self._entrees[cle] = (corps, mime)
            self._octets += len(corps)
            while self._octets > self.octets_max and self._entrees:
                _, evincee = self._entrees.popitem(last=False)
                self._octets -= len(evincee[0])

    def vider(self) -> None:
        with self._verrou:
            self._entrees.clear()
            self._octets = 0

    def compteurs(self) -> dict:
        with self._verrou:
            return {
                "vignettes": len(self._entrees),
                "ko": self._octets // 1024,
                "touches": self.touches,
                "manques": self.manques,
            }


class Vignettes:
    def __init__(self, dossier: str | Path, *, agent: str, delai: float = DELAI_PHOTO,
                 memoire_max: int = MEMOIRE_PAR_DEFAUT,
                 ouvriers: int = OUVRIERS) -> None:
        self.memoire = Memoire(memoire_max)
        self.dossier = Path(dossier)
        self.sources = self.dossier / "source"
        self.vignettes = self.dossier / "vignettes"
        for chemin in (self.sources, self.vignettes):
            chemin.mkdir(parents=True, exist_ok=True)
        self.agent = agent
        self.delai = delai
        # Deux ouvriers, pas plus : le but est de preparer une photo pendant
        # que l'utilisateur lit l'ecran, pas d'ouvrir une rafale de connexions
        # vers Open Food Facts. `ouvriers=0` desactive le prechauffage --
        # utile pour mesurer le cache sans travail de fond dans le dos.
        self._fond = (concurrent.futures.ThreadPoolExecutor(
            max_workers=ouvriers, thread_name_prefix="prechauffe")
            if ouvriers > 0 else None)
        self._en_vol: set[str] = set()
        self._verrou_vol = threading.Lock()
        self._urls: dict[str, threading.Lock] = {}
        self._verrou_urls = threading.Lock()

    # ----------------------------------------------------------- prechauffage

    def prechauffer(self, code: str, url: str,
                    tailles=TAILLES_TERMINAL, format_: str = FORMAT_PAR_DEFAUT) -> int:
        """Prepare en tache de fond les vignettes que le terminal va demander.

        Appele des qu'une fiche produit est resolue. Rend le nombre de taches
        effectivement mises en file -- zero si tout est deja en cache.

        Les erreurs sont avalees : un prechauffage rate n'est pas une panne,
        la demande suivante refera le travail en direct.
        """
        if not url or not PILLOW_DISPONIBLE or self._fond is None:
            return 0
        mises = 0
        for taille in tailles:
            cle = f"{code}:{taille}:{format_}"
            with self._verrou_vol:
                if cle in self._en_vol:
                    continue
                self._en_vol.add(cle)
            self._fond.submit(self._prechauffer_une, cle, code, url, taille, format_)
            mises += 1
        return mises

    def _prechauffer_une(self, cle: str, code: str, url: str,
                         taille: int, format_: str) -> None:
        try:
            self.obtenir(code, url, taille, taille, format_)
        except Exception as exc:                     # pragma: no cover
            log.debug("prechauffage sans effet pour %s : %s", code, exc)
        finally:
            with self._verrou_vol:
                self._en_vol.discard(cle)

    def arreter(self) -> None:
        if self._fond is not None:
            self._fond.shutdown(wait=False, cancel_futures=True)

    # ----------------------------------------------------------------- source

    def _original(self, url: str) -> bytes | None:
        """L'image source, telechargee une seule fois quoi qu'il arrive.

        Le verrou par URL n'est pas une precaution theorique. Depuis que la
        photo part en prechauffage des le scan, deux fils la reclament dans la
        meme seconde : celui du fond, et celui de la requete du terminal quand
        l'ecran la demande. Sans verrou, chacun ouvrait sa connexion et
        telechargeait le meme fichier -- trois fois pour les trois tailles --
        ce qui ralentissait precisement ce que le prechauffage devait
        accelerer. Le premier arrive telecharge, les autres attendent et
        relisent le cache.
        """
        empreinte = hashlib.sha1(url.encode("utf-8")).hexdigest()[:20]
        cache = self.sources / empreinte
        if cache.is_file():
            return cache.read_bytes()

        with self._verrou_urls:
            verrou = self._urls.setdefault(empreinte, threading.Lock())
        with verrou:
            # Un autre fil a pu terminer pendant qu'on attendait.
            if cache.is_file():
                return cache.read_bytes()
            donnees = self._telecharger(url, cache)
        with self._verrou_urls:
            self._urls.pop(empreinte, None)
        return donnees

    def _telecharger(self, url: str, cache: Path) -> bytes | None:
        requete = urllib.request.Request(url, headers={
            "User-Agent": self.agent, "Accept": "image/*",
        })
        try:
            with urllib.request.urlopen(requete, timeout=self.delai) as reponse:
                donnees = reponse.read(TAILLE_MAX_SOURCE + 1)
        except (urllib.error.URLError, TimeoutError, OSError) as exc:
            log.info("photo injoignable (%s) : %s", url, exc)
            return None
        if not donnees or len(donnees) > TAILLE_MAX_SOURCE:
            return None
        # Ecriture atomique : un cache tronque par une coupure serait pire
        # qu'un cache vide, car il ne serait jamais retelecharge.
        provisoire = _provisoire(cache)
        provisoire.write_bytes(donnees)
        provisoire.replace(cache)
        return donnees

    # -------------------------------------------------------------- vignette

    def obtenir(self, code: str, url: str, largeur: int, hauteur: int,
                format_: str = "bmp") -> tuple[bytes, str] | None:
        """Vignette prete a l'affichage, ou None si rien n'est exploitable."""
        if not url:
            return None
        format_ = format_ if format_ in FORMATS else "bmp"
        mime, extension = FORMATS[format_]
        largeur = max(16, min(int(largeur), 480))
        hauteur = max(16, min(int(hauteur), 480))

        empreinte = hashlib.sha1(url.encode("utf-8")).hexdigest()[:12]
        cle = f"{code}-{empreinte}-{largeur}x{hauteur}.{extension}"
        en_memoire = self.memoire.lire(cle)
        if en_memoire is not None:
            return en_memoire

        cible = self.vignettes / cle
        if cible.is_file():
            rendu = cible.read_bytes()
            self.memoire.ecrire(cle, rendu, mime)
            return rendu, mime

        donnees = self._original(url)
        if donnees is None:
            return None
        if not PILLOW_DISPONIBLE:
            # Sans Pillow on ne sait ni retailler ni convertir. Renvoyer le
            # JPEG d'origine laisse une chance au terminal, mais c'est un
            # mode degrade : le README le dit, et l'API le signale.
            return (donnees, "image/jpeg") if format_ == "jpg" else None

        try:
            vignette = self._retailler(donnees, largeur, hauteur)
        except Exception as exc:                     # image corrompue, format exotique
            log.info("photo illisible pour %s : %s", code, exc)
            return None

        tampon = io.BytesIO()
        if format_ == "jpg":
            vignette.save(tampon, "JPEG", quality=82)
        elif format_ == "png":
            vignette.save(tampon, "PNG", optimize=True)
        elif format_ == "bmp8":
            # Palette adaptative : les 256 couleurs sont choisies dans l'image
            # plutot que dans une grille fixe, et le tramage de Floyd-Steinberg
            # rattrape les degrades d'un emballage. Sur 80 pixels de cote, la
            # difference avec les couleurs vraies ne se voit pas.
            vignette.convert("P", palette=Image.ADAPTIVE, colors=256,
                             dither=Image.FLOYDSTEINBERG).save(tampon, "BMP")
        else:
            vignette.save(tampon, "BMP")             # 24 bits, non compresse
        rendu = tampon.getvalue()
        provisoire = _provisoire(cible)
        provisoire.write_bytes(rendu)
        provisoire.replace(cible)
        self.memoire.ecrire(cle, rendu, mime)
        return rendu, mime

    @staticmethod
    def _retailler(donnees: bytes, largeur: int, hauteur: int):
        """Cadre exact largeur x hauteur, image entiere, fond blanc.

        Le terminal recoit ainsi toujours la meme taille : il reserve son
        cadre une fois pour toutes et ne recalcule aucune mise en page.
        """
        source = Image.open(io.BytesIO(donnees))
        source.load()
        if source.mode in ("RGBA", "LA", "P"):
            source = source.convert("RGBA")
            fond = Image.new("RGB", source.size, (255, 255, 255))
            fond.paste(source, mask=source.split()[-1])
            source = fond
        else:
            source = source.convert("RGB")

        source.thumbnail((largeur, hauteur), Image.LANCZOS)
        cadre = Image.new("RGB", (largeur, hauteur), (255, 255, 255))
        cadre.paste(source, ((largeur - source.width) // 2,
                             (hauteur - source.height) // 2))
        return cadre

    def vider(self) -> int:
        self.memoire.vider()
        n = 0
        for dossier in (self.sources, self.vignettes):
            for fichier in dossier.iterdir():
                if fichier.is_file():
                    fichier.unlink()
                    n += 1
        return n

    def balayer_provisoires(self) -> int:
        """Supprime les fichiers .part laisses par un arret brutal."""
        n = 0
        for dossier in (self.sources, self.vignettes):
            for fichier in dossier.glob("*.part"):
                try:
                    fichier.unlink()
                    n += 1
                except OSError:                      # pragma: no cover
                    pass
        return n
