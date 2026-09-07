"""Transliteration fidele du code C# du terminal, pour verifier l'interoperabilite.

Ce module n'est pas utilise en production : il reproduit ligne par ligne
client/SkorpioFrigo/Protocol.cs et client/SkorpioFrigo/Dates.cs afin que
tests/test_interop.py puisse comparer les deux implementations sans compilateur
C# sous la main. Une divergence de checksum ou d'analyse de date casserait la
liaison sans le moindre message d'erreur : c'est exactement le genre de panne
qu'on ne veut pas decouvrir avec le terminal en main.

Toute modification du C# doit etre repercutee ici, sinon les tests deviennent
un faux temoin.
"""

from __future__ import annotations

import calendar
import datetime as dt

SEP = "|"
CHECKSUM_MARK = "*"
VERSION = "1"


# --------------------------------------------------------------- Protocol.cs


def cs_checksum(payload: str) -> str:
    acc = 0
    for b in payload.encode("utf-8"):
        acc ^= b
    return format(acc, "02X")


def cs_sanitize(value: str | None) -> str:
    if value is None:
        return ""
    out = []
    for c in value:
        if c == SEP or c == CHECKSUM_MARK or c < " ":
            out.append(" ")
        else:
            out.append(c)
    return "".join(out).strip()


def cs_encode(*parts: str) -> str:
    sb = []
    for i, part in enumerate(parts):
        if i > 0:
            sb.append(SEP)
        sb.append(cs_sanitize(part).upper() if i == 0 else cs_sanitize(part))
    payload = "".join(sb)
    return payload + SEP + CHECKSUM_MARK + cs_checksum(payload)


class CsFormatError(Exception):
    """Equivalent du FormatException leve par Protocol.Decode."""


def cs_decode(line: str | None) -> list[str]:
    if line is None:
        raise CsFormatError("ligne vide")
    text = line.strip()
    if len(text) == 0:
        raise CsFormatError("ligne vide")

    raw = text.split(SEP)
    count = len(raw)
    if count > 1 and len(raw[count - 1]) > 0 and raw[count - 1][0] == CHECKSUM_MARK:
        received = raw[count - 1][1:].upper()
        count -= 1
        payload = SEP.join(raw[:count])
        expected = cs_checksum(payload)
        if received != expected:
            raise CsFormatError(f"checksum {received} attendue {expected}")

    fields = [raw[i].strip() for i in range(count)]
    fields[0] = fields[0].upper()
    return fields


# ------------------------------------------------------------------ Dates.cs


def _only_digits(text: str | None) -> str:
    if text is None:
        return ""
    return "".join(c for c in text if "0" <= c <= "9")


def _expand_year(yy: int, today: dt.date) -> int:
    century = today.year - (today.year % 100)
    year = century + yy
    if year < today.year - 10:
        year += 100
    return year


def _build(y: int, m: int, d: int) -> dt.date:
    if m < 1 or m > 12:
        raise CsFormatError(f"mois {m} invalide")
    if y < 1970 or y > 2999:
        raise CsFormatError(f"annee {y} invalide")
    if d < 1 or d > calendar.monthrange(y, m)[1]:
        raise CsFormatError(f"jour {d} invalide pour ce mois")
    return dt.date(y, m, d)


def cs_try_parse(text: str, relative: bool, today: dt.date | None = None) -> dt.date | None:
    """Retourne None pour 'date inconnue', leve CsFormatError si refuse."""
    today = today or dt.date.today()
    digits = _only_digits(text)
    if len(digits) == 0:
        return None

    if relative:
        days = int(digits)
        if days < 0 or days > 3650:
            raise CsFormatError("nombre de jours invalide")
        return today + dt.timedelta(days=days)

    if len(digits) == 4:                                    # JJMM
        d, m = int(digits[0:2]), int(digits[2:4])
        result = _build(today.year, m, d)
        if result < today:
            result = _build(today.year + 1, m, d)
        return result
    if len(digits) == 6:                                    # JJMMAA
        d, m, y = int(digits[0:2]), int(digits[2:4]), int(digits[4:6])
        return _build(_expand_year(y, today), m, d)
    if len(digits) == 8:                                    # JJMMAAAA
        d, m, y = int(digits[0:2]), int(digits[2:4]), int(digits[4:8])
        return _build(y, m, d)
    raise CsFormatError("attendu JJMM, JJMMAA ou JJMMAAAA")


def cs_to_iso(value: dt.date | None) -> str:
    return "" if value is None else value.isoformat()
