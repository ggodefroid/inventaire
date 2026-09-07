"""Protocole de ligne entre le client Skorpio et le serveur PC.

Un seul format sert les trois transports (USB/serie, TCP, fichier sur carte
memoire) : des lignes ASCII terminees par CRLF, champs separes par '|', avec
une somme de controle XOR facultative en fin de ligne.

    SCAN|12|3017620422003|2026-10-25|1|frigo|2026-09-07T18:04:11|*4F
    ^    ^  ^             ^          ^ ^     ^                    ^
    |    |  code-barres   peremption | lieu  horodatage terminal  checksum
    |    numero de sequence          quantite
    verbe

Le couple (device, seq) rend l'ingestion idempotente : reposer le terminal sur
son socle et renvoyer tout le tampon ne cree jamais de doublon.

Terminal -> serveur
    HELLO|<device>|<proto>|<version_client>
    SCAN|<seq>|<code>|<peremption>|<qte>|<lieu>|<horodatage>|<note>
    LOOK|<code>                      demande de libelle produit
    BYE|<nb_scans_envoyes>
    PING

Serveur -> terminal
    READY|<proto>|<version_serveur>|<device>
    OK|<seq>|<libelle>               scan enregistre
    DUP|<seq>                        deja connu, ignore
    ERR|<seq>|<motif>
    INFO|<code>|<libelle>|<marque>
    PONG
"""

from __future__ import annotations

import datetime as dt
from dataclasses import dataclass, field

__all__ = [
    "PROTO_VERSION",
    "FIELD_SEP",
    "LINE_END",
    "Message",
    "ProtocolError",
    "encode",
    "decode",
    "checksum",
    "sanitize",
    "now_iso",
]

PROTO_VERSION = "1"
FIELD_SEP = "|"
LINE_END = "\r\n"
CHECKSUM_MARK = "*"
MAX_LINE = 1024


class ProtocolError(ValueError):
    """Ligne inexploitable (verbe inconnu, checksum fausse, ligne tronquee)."""


@dataclass(slots=True)
class Message:
    verb: str
    args: list[str] = field(default_factory=list)

    def arg(self, index: int, default: str = "") -> str:
        try:
            return self.args[index]
        except IndexError:
            return default

    def __str__(self) -> str:
        return encode(self.verb, *self.args, with_checksum=False)


def checksum(payload: str) -> str:
    """XOR de tous les octets de la charge utile, sur deux chiffres hex."""
    acc = 0
    for byte in payload.encode("utf-8", "replace"):
        acc ^= byte
    return f"{acc:02X}"


def sanitize(value: object) -> str:
    """Rend une valeur transportable : pas de separateur, pas de caractere de controle."""
    if value is None:
        return ""
    text = str(value)
    out = []
    for ch in text:
        if ch == FIELD_SEP or ch == CHECKSUM_MARK or ord(ch) < 0x20:
            out.append(" ")
        else:
            out.append(ch)
    return "".join(out).strip()


def encode(verb: str, *args: object, with_checksum: bool = True) -> str:
    """Construit une ligne (sans le CRLF final)."""
    payload = FIELD_SEP.join([verb.upper()] + [sanitize(a) for a in args])
    if not with_checksum:
        return payload
    return f"{payload}{FIELD_SEP}{CHECKSUM_MARK}{checksum(payload)}"


def decode(line: str, *, strict_checksum: bool = False) -> Message:
    """Analyse une ligne recue. La somme de controle est verifiee si presente."""
    if line is None:
        raise ProtocolError("ligne vide")
    text = line.strip("\r\n").strip()
    if not text:
        raise ProtocolError("ligne vide")
    if len(text) > MAX_LINE:
        raise ProtocolError(f"ligne trop longue ({len(text)} caracteres)")

    parts = text.split(FIELD_SEP)
    if parts and parts[-1].startswith(CHECKSUM_MARK):
        received = parts.pop()[len(CHECKSUM_MARK):].upper()
        payload = FIELD_SEP.join(parts)
        expected = checksum(payload)
        if received != expected:
            raise ProtocolError(f"checksum {received} attendue {expected}")
    elif strict_checksum:
        raise ProtocolError("checksum absente")

    verb = parts[0].strip().upper()
    if not verb.isalpha():
        raise ProtocolError(f"verbe invalide : {parts[0]!r}")
    return Message(verb=verb, args=[p.strip() for p in parts[1:]])


def now_iso() -> str:
    """Horodatage local a la seconde, sans fuseau (le terminal n'en a pas)."""
    return dt.datetime.now().replace(microsecond=0).isoformat()
