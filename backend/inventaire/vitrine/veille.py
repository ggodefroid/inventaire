"""Detection des mouvements du frigo, et diffusion aux navigateurs connectes.

Le site ne touche jamais a la base : c'est le serveur du terminal qui ecrit,
dans un autre processus. Il faut donc apprendre le changement sans etre
prevenu. `PRAGMA data_version` le dit : SQLite incremente cet entier des
qu'une autre connexion valide une transaction. Un thread le relit chaque
seconde, ce qui coute une lecture de page deja en cache.

Quand la valeur bouge, l'instantane est recalcule **une fois**, puis pousse a
tous les abonnes. Dix visiteurs ne font pas dix calculs.

Chaque abonne a sa propre file bornee plutot qu'une reference directe a sa
WebSocket : `send` depuis le thread de veille traverserait la connexion d'un
autre thread. Le thread du client est donc le seul a ecrire sur sa socket.
Une file pleine signale un client trop lent : on laisse tomber le message,
il se resynchronisera sur le numero de revision du prochain pouls.
"""

from __future__ import annotations

import datetime as dt
import hashlib
import json
import logging
import queue
import threading
import time

from . import mesures

log = logging.getLogger("vitrine.veille")

PERIODE = 1.0            # intervalle de scrutation de data_version, en secondes
POULS = 5.0              # intervalle du battement envoye aux navigateurs
RECALCUL = 60.0          # recalcul force : les echeances vieillissent seules
FILE_MAX = 8


class Veille:
    def __init__(self, lecture, *, periode: float = PERIODE) -> None:
        self.lecture = lecture
        self.periode = periode
        self.debut = time.time()
        self.revision = 0
        self.recalculs = 0
        self._abonnes: set[queue.Queue] = set()
        self._verrou = threading.Lock()
        self._etat: dict = {}
        self._empreinte = ""
        self._version = -2
        self._dernier_calcul = 0.0
        self._dernier_pouls = time.monotonic()
        self._arret = threading.Event()
        self._thread: threading.Thread | None = None
        self.rafraichir()

    # ------------------------------------------------------------ instantane

    def rafraichir(self) -> dict:
        """Recalcule l'instantane. Retourne l'etat, change ou non.

        La version de la base est relevee **avant** la lecture, jamais apres :
        une ecriture qui tombe pendant le calcul serait sinon deja comprise
        dans le resultat tout en marquant la version comme vue, et le
        changement suivant serait le seul a etre diffuse. Releve en premier,
        elle fait au pire un calcul de trop.
        """
        version = self.lecture.version_donnees()
        try:
            etat = mesures.instantane(self.lecture)
        except Exception:
            log.exception("instantane impossible")
            etat = self._etat or _etat_degrade()
        empreinte = _empreinte(etat)
        with self._verrou:
            self.recalculs += 1
            self._version = version
            self._dernier_calcul = time.monotonic()
            if empreinte != self._empreinte:
                self.revision += 1
                self._empreinte = empreinte
            etat["revision"] = self.revision
            self._etat = etat
        return etat

    def rafraichir_et_pousser(self) -> int:
        """Recalcule maintenant et diffuse si quelque chose a bouge.

        La boucle de veille scrute `data_version` chaque seconde : elle
        finirait par voir le changement. Mais quand c'est le navigateur
        lui-meme qui vient de cocher un article, une seconde d'attente se voit.
        Rend la revision obtenue.
        """
        avant = self.revision
        self.rafraichir()
        if self.revision != avant:
            self._diffuser(self.message("maj"))
        return self.revision

    def etat(self) -> dict:
        """L'etat courant, horloge remise a l'heure de la requete."""
        with self._verrou:
            etat = dict(self._etat)
        etat["horloge"] = _horloge()
        etat["revision"] = self.revision
        return etat

    def message(self, type_: str) -> str:
        with self._verrou:
            etat = dict(self._etat)
        etat["type"] = type_
        etat["revision"] = self.revision
        etat["horloge"] = _horloge()
        return json.dumps(etat, ensure_ascii=False, default=str)

    def pouls(self) -> str:
        maintenant = dt.datetime.now()
        with self._verrou:
            compteurs = self._etat.get("compteurs", {})
            abonnes = len(self._abonnes)
        return json.dumps({
            "type": "pouls",
            "revision": self.revision,
            "heure": maintenant.strftime("%H:%M:%S"),
            "date": maintenant.date().isoformat(),
            "clients": abonnes,
            "uptime": int(time.time() - self.debut),
            "recalculs": self.recalculs,
            "unites": compteurs.get("unites", 0),
        }, ensure_ascii=False)

    # --------------------------------------------------------------- abonnes

    def abonner(self) -> queue.Queue:
        fil: queue.Queue = queue.Queue(maxsize=FILE_MAX)
        with self._verrou:
            self._abonnes.add(fil)
        return fil

    def desabonner(self, fil: queue.Queue) -> None:
        with self._verrou:
            self._abonnes.discard(fil)

    def clients(self) -> int:
        with self._verrou:
            return len(self._abonnes)

    def _diffuser(self, message: str) -> None:
        with self._verrou:
            abonnes = list(self._abonnes)
        for fil in abonnes:
            try:
                fil.put_nowait(message)
            except queue.Full:
                pass          # client a la traine : il se rattrapera sur la revision

    # ---------------------------------------------------------------- boucle

    def demarrer(self) -> None:
        if self._thread is not None:
            return
        self._thread = threading.Thread(target=self._boucle, name="veille", daemon=True)
        self._thread.start()

    def arreter(self) -> None:
        self._arret.set()
        if self._thread is not None:
            self._thread.join(timeout=2.0)
            self._thread = None
        self.lecture.fermer()

    def _boucle(self) -> None:
        while not self._arret.wait(self.periode):
            try:
                self._tour()
            except Exception:
                log.exception("tour de veille echoue")

    def _tour(self) -> None:
        version = self.lecture.version_donnees()
        ecoule = time.monotonic() - self._dernier_calcul
        bouge = version != self._version and version != -1
        if bouge or ecoule >= RECALCUL:
            avant = self.revision
            self.rafraichir()
            if self.revision != avant:
                self._diffuser(self.message("maj"))
                log.info("revision %s diffusee a %s client(s)",
                         self.revision, self.clients())
        if time.monotonic() - self._dernier_pouls >= POULS:
            self._dernier_pouls = time.monotonic()
            if self._abonnes:
                self._diffuser(self.pouls())


def _empreinte(etat: dict) -> str:
    """Signature de l'etat, horloge exclue : sinon tout change chaque seconde."""
    sans_horloge = {cle: valeur for cle, valeur in etat.items()
                    if cle not in ("horloge", "revision")}
    brut = json.dumps(sans_horloge, sort_keys=True, ensure_ascii=False, default=str)
    return hashlib.blake2b(brut.encode("utf-8"), digest_size=16).hexdigest()


def _horloge() -> dict:
    maintenant = dt.datetime.now()
    return {
        "iso": maintenant.isoformat(timespec="seconds"),
        "date": maintenant.date().isoformat(),
        "heure": maintenant.strftime("%H:%M:%S"),
        "semaine": maintenant.isocalendar().week,
        "jour_annee": maintenant.timetuple().tm_yday,
    }


def _etat_degrade() -> dict:
    return {
        "horloge": _horloge(),
        "compteurs": {"unites": 0, "references": 0, "lots": 0, "produits": 0,
                      "fiches": 0, "manuels": 0, "mouvements": 0, "alias": 0,
                      "perimes": 0, "premier_mouvement": "", "dernier_mouvement": ""},
        "kpi": [], "chiffres": [], "curiosites": [],
        "donuts": {}, "barres": {}, "series": {}, "niveaux": {},
        "urgents": [], "postes": [], "journal": [],
        "systeme": {"lecture_seule": True, "base": "indisponible", "octets": 0},
    }
