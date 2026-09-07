"""Coeur d'ingestion : applique les lignes de protocole a la base.

Une instance de Session correspond a une conversation avec un terminal
(une connexion serie, une connexion TCP, ou un fichier deverse depuis la carte
memoire). Le meme code sert les trois transports : c'est ce qui garantit qu'un
scan enregistre hors ligne puis synchronise sur socle produit exactement le
meme resultat qu'un scan temps reel.
"""

from __future__ import annotations

import datetime as dt
import logging

from . import barcode as bc
from .dates import ExpiryError, parse_expiry
from .db import Store
from .products import Enricher, resolve
from .protocol import PROTO_VERSION, Message, ProtocolError, decode, encode

__all__ = ["Session", "SessionStats"]

log = logging.getLogger("frigo.ingest")
SERVER_VERSION = "0.1.0"


class SessionStats:
    __slots__ = ("ok", "dup", "err", "lines")

    def __init__(self) -> None:
        self.ok = self.dup = self.err = self.lines = 0

    def __str__(self) -> str:
        return (f"{self.lines} ligne(s) : {self.ok} enregistre(s), "
                f"{self.dup} doublon(s), {self.err} rejet(s)")


class Session:
    """Traite les lignes d'un terminal et renvoie les lignes de reponse."""

    def __init__(
        self,
        store: Store,
        *,
        channel: str = "usb",
        online: bool = True,
        enricher: Enricher | None = None,
        default_location: str = "frigo",
        device: str = "SKORPIO",
    ) -> None:
        self.store = store
        self.channel = channel
        self.online = online
        self.enricher = enricher
        self.default_location = default_location
        self.device = device
        self.stats = SessionStats()

    # ------------------------------------------------------------------ entree

    def handle_line(self, line: str) -> list[str]:
        """Traite une ligne brute. Retourne les lignes a renvoyer au terminal."""
        raw = (line or "").strip()
        if not raw:
            return []
        self.stats.lines += 1
        try:
            msg = decode(raw)
        except ProtocolError as exc:
            self.stats.err += 1
            self.store.log(device=self.device, channel=self.channel, raw=raw,
                           verdict="err", detail=str(exc))
            log.warning("ligne rejetee (%s) : %r", exc, raw[:120])
            return [encode("ERR", "-", str(exc))]

        handler = getattr(self, f"_do_{msg.verb.lower()}", None)
        if handler is None:
            self.stats.err += 1
            self.store.log(device=self.device, channel=self.channel, raw=raw,
                           verdict="err", detail="verbe inconnu")
            return [encode("ERR", "-", f"verbe inconnu {msg.verb}")]
        return handler(msg, raw)

    def handle_text(self, text: str) -> list[str]:
        """Traite un bloc multi-lignes (fichier de carte memoire)."""
        out: list[str] = []
        for line in text.splitlines():
            out.extend(self.handle_line(line))
        return out

    # ------------------------------------------------------------------ verbes

    def _do_hello(self, msg: Message, raw: str) -> list[str]:
        device = msg.arg(0) or self.device
        self.device = "".join(c for c in device if c.isalnum() or c in "-_")[:32] or "SKORPIO"
        proto = msg.arg(1, PROTO_VERSION)
        client = msg.arg(2, "?")
        log.info("terminal connecte : %s (protocole %s, client %s) via %s",
                 self.device, proto, client, self.channel)
        if proto != PROTO_VERSION:
            return [encode("ERR", "-", f"protocole {proto} non supporte "
                                       f"(serveur en {PROTO_VERSION})")]
        # On renvoie le dernier numero de sequence connu : le terminal sait ainsi
        # ou reprendre et peut purger son tampon jusque-la.
        return [encode("READY", PROTO_VERSION, SERVER_VERSION, self.device,
                       self.store.last_seq(self.device))]

    def _do_ping(self, msg: Message, raw: str) -> list[str]:
        return [encode("PONG")]

    def _do_look(self, msg: Message, raw: str) -> list[str]:
        code = bc.normalize(msg.arg(0))
        if not code:
            return [encode("ERR", "-", "code vide")]
        label = resolve(self.store, code, online=self.online)
        row = self.store.product(code)
        brand = row["brand"] if row and row["brand"] else ""
        return [encode("INFO", code, label or "inconnu", brand)]

    def _do_bye(self, msg: Message, raw: str) -> list[str]:
        log.info("fin de session %s : %s", self.device, self.stats)
        if self.enricher:
            self.enricher.nudge()
        return [encode("BYE", self.stats.ok, self.stats.dup, self.stats.err)]

    def _do_scan(self, msg: Message, raw: str) -> list[str]:
        seq_raw = msg.arg(0)
        code = bc.normalize(msg.arg(1))

        if not code:
            return self._reject(seq_raw, raw, "code-barres vide")

        try:
            seq = int(seq_raw) if seq_raw not in ("", "-") else None
        except ValueError:
            return self._reject(seq_raw, raw, f"sequence invalide {seq_raw!r}")

        try:
            expiry = parse_expiry(msg.arg(2))
        except ExpiryError as exc:
            return self._reject(seq_raw, raw, str(exc))

        try:
            qty = int(msg.arg(3) or 1)
        except ValueError:
            return self._reject(seq_raw, raw, f"quantite invalide {msg.arg(3)!r}")
        if not 1 <= qty <= 999:
            return self._reject(seq_raw, raw, f"quantite hors bornes : {qty}")

        location = msg.arg(4) or self.default_location
        scanned_at = msg.arg(5) or None
        note = msg.arg(6) or None

        verdict, item_id = self.store.add_item(
            code,
            expiry=expiry.isoformat() if expiry else None,
            qty=qty,
            location=location[:24],
            note=note,
            device=self.device,
            seq=seq,
            scanned_at=scanned_at,
        )

        if verdict == "dup":
            self.stats.dup += 1
            self.store.log(device=self.device, channel=self.channel, raw=raw,
                           verdict="dup", detail=f"item {item_id}")
            return [encode("DUP", seq_raw)]

        self.stats.ok += 1
        # Cache uniquement : le reseau ne doit pas ralentir la liaison serie.
        label = self.store.product_label(code)
        self.store.log(device=self.device, channel=self.channel, raw=raw,
                       verdict="ok", detail=f"item {item_id} {code} {expiry or 'sans date'}")
        if self.enricher and not label:
            self.enricher.nudge()
        return [encode("OK", seq_raw, label or code)]

    # ------------------------------------------------------------------ helpers

    def _reject(self, seq_raw: str, raw: str, motif: str) -> list[str]:
        self.stats.err += 1
        self.store.log(device=self.device, channel=self.channel, raw=raw,
                       verdict="err", detail=motif)
        log.warning("scan rejete (%s) : %r", motif, raw[:120])
        return [encode("ERR", seq_raw or "-", motif)]
