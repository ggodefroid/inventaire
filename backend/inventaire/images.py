"""Proxy de photos : HTTPS chez Open Food Facts, BMP non compresse pour CE.

Trois raisons de ne pas laisser le terminal chercher l'image lui-meme :

* elle est servie en HTTPS, que la pile de Windows CE 5.0 ne negocie plus ;
* elle fait 200 px de cote en JPEG, quand l'ecran lui en reserve environ 80 ;
* le decodage JPEG du Compact Framework depend de codecs qui peuvent tout
  simplement manquer de l'image OS du terminal -- un `Bitmap(flux)` renvoie
  alors une exception, pas une image degradee.

On envoie donc un BMP 24 bits sans compression, a la taille exacte du cadre.
C'est plus lourd sur le fil (une vignette de 88 px pese 23 ko) mais c'est
negligeable en Wi-Fi, et surtout c'est un format que le terminal sait decoder
sans rien de plus -- et, au pire, qu'il peut lire octet par octet lui-meme.

Le cache disque est a deux etages : l'original tel que recu, et chaque taille
demandee. Rebipper un produit n'appelle donc plus rien.
"""

from __future__ import annotations

import collections
import hashlib
import io
import logging
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
    "bmp": ("image/bmp", "bmp"),
    "jpg": ("image/jpeg", "jpg"),
    "png": ("image/png", "png"),
}

TAILLE_MAX_SOURCE = 4 * 1024 * 1024                  # garde-fou sur le telechargement
MEMOIRE_PAR_DEFAUT = 24 * 1024 * 1024               # cache en RAM des vignettes


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
    def __init__(self, dossier: str | Path, *, agent: str, delai: float = 10.0,
                 memoire_max: int = MEMOIRE_PAR_DEFAUT) -> None:
        self.memoire = Memoire(memoire_max)
        self.dossier = Path(dossier)
        self.sources = self.dossier / "source"
        self.vignettes = self.dossier / "vignettes"
        for chemin in (self.sources, self.vignettes):
            chemin.mkdir(parents=True, exist_ok=True)
        self.agent = agent
        self.delai = delai

    # ----------------------------------------------------------------- source

    def _original(self, url: str) -> bytes | None:
        empreinte = hashlib.sha1(url.encode("utf-8")).hexdigest()[:20]
        cache = self.sources / empreinte
        if cache.is_file():
            return cache.read_bytes()
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
        provisoire = cache.with_suffix(".part")
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
        else:
            vignette.save(tampon, "BMP")             # 24 bits, non compresse
        rendu = tampon.getvalue()
        provisoire = cible.with_suffix(cible.suffix + ".part")
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
