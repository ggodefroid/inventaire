"""Analyse des dates de peremption saisies au pave numerique du terminal.

Le clavier du Skorpio est avant tout numerique : on accepte donc des saisies
courtes et sans separateur, ainsi que des raccourcis relatifs.

    ""            -> None (pas de date connue)
    "+7" / "7j"   -> aujourd'hui + 7 jours
    "2510"        -> 25 octobre (prochaine occurrence)
    "251026"      -> 25/10/2026
    "25102026"    -> 25/10/2026
    "1026"        -> ambigu : traite comme jour/mois (10 octobre)
    "10/2026"     -> 31/10/2026 (fin de mois, pour les DDM type "10/2026")
    "2026-10-25"  -> 25/10/2026
"""

from __future__ import annotations

import calendar
import datetime as dt
import re

__all__ = ["parse_expiry", "format_expiry", "days_left", "ExpiryError"]


class ExpiryError(ValueError):
    """Saisie de date impossible a interpreter."""


_REL = re.compile(r"^\+?(\d{1,4})\s*(?:j|jours?|d|days?)?$", re.I)
_ISO = re.compile(r"^(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})$")
_DMY = re.compile(r"^(\d{1,2})[-/.](\d{1,2})[-/.](\d{2}|\d{4})$")
_MY = re.compile(r"^(\d{1,2})[-/.](\d{4})$")


def _end_of_month(year: int, month: int) -> dt.date:
    return dt.date(year, month, calendar.monthrange(year, month)[1])


def _expand_year(yy: int, today: dt.date) -> int:
    """Complete une annee sur 2 chiffres dans une fenetre -10/+89 ans."""
    century = today.year - today.year % 100
    year = century + yy
    if year < today.year - 10:
        year += 100
    return year


def parse_expiry(text: str | None, today: dt.date | None = None) -> dt.date | None:
    """Convertit une saisie terminal en date. Retourne None si vide."""
    today = today or dt.date.today()
    if text is None:
        return None
    raw = text.strip()
    if not raw or raw in {"-", "0"}:
        return None

    # Raccourci relatif : "+7", "7j", "30"...  (uniquement si prefixe +, ou <= 3
    # chiffres qui ne ressemblent pas a une date compacte)
    m = _REL.match(raw)
    if m and (raw.startswith("+") or len(m.group(1)) <= 3):
        return today + dt.timedelta(days=int(m.group(1)))

    if m := _ISO.match(raw):
        y, mo, d = (int(g) for g in m.groups())
        return _safe_date(y, mo, d)

    if m := _DMY.match(raw):
        d, mo, y = (int(g) for g in m.groups())
        if len(m.group(3)) == 2:
            y = _expand_year(y, today)
        return _safe_date(y, mo, d)

    if m := _MY.match(raw):
        mo, y = int(m.group(1)), int(m.group(2))
        if not 1 <= mo <= 12:
            raise ExpiryError(f"mois invalide dans {raw!r}")
        return _end_of_month(y, mo)

    digits = re.sub(r"\D", "", raw)
    if digits != raw and not digits:
        raise ExpiryError(f"date illisible : {raw!r}")

    if len(digits) == 4:  # DDMM
        d, mo = int(digits[:2]), int(digits[2:])
        if not 1 <= mo <= 12:
            # peut-etre MMYY saisi a l'envers -> fin de mois
            mo2, yy = int(digits[:2]), int(digits[2:])
            if 1 <= mo2 <= 12:
                return _end_of_month(_expand_year(yy, today), mo2)
            raise ExpiryError(f"date illisible : {raw!r}")
        candidate = _safe_date(today.year, mo, d)
        if candidate < today:  # une DDM est presque toujours dans le futur
            candidate = _safe_date(today.year + 1, mo, d)
        return candidate

    if len(digits) == 6:  # DDMMYY
        d, mo, yy = int(digits[:2]), int(digits[2:4]), int(digits[4:])
        return _safe_date(_expand_year(yy, today), mo, d)

    if len(digits) == 8:  # DDMMYYYY
        d, mo, y = int(digits[:2]), int(digits[2:4]), int(digits[4:])
        return _safe_date(y, mo, d)

    raise ExpiryError(f"date illisible : {raw!r}")


def _safe_date(year: int, month: int, day: int) -> dt.date:
    try:
        return dt.date(year, month, day)
    except ValueError as exc:
        raise ExpiryError(f"date inexistante : {day:02d}/{month:02d}/{year}") from exc


def format_expiry(value: dt.date | str | None) -> str:
    """Affichage court pour le terminal (8 caracteres)."""
    if value is None:
        return "-"
    if isinstance(value, str):
        try:
            value = dt.date.fromisoformat(value)
        except ValueError:
            return value
    return value.strftime("%d/%m/%y")


def days_left(value: dt.date | str | None, today: dt.date | None = None) -> int | None:
    """Nombre de jours restants (negatif si perime)."""
    if value is None:
        return None
    if isinstance(value, str):
        try:
            value = dt.date.fromisoformat(value)
        except ValueError:
            return None
    return (value - (today or dt.date.today())).days
