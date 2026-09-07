"""Normalisation des codes-barres lus par le scanner laser du Skorpio.

Le lecteur laser du Skorpio-G lit surtout de l'EAN-13 / EAN-8 / UPC-A, plus
d'eventuels Code39/Code128 pour les etiquettes maison. On normalise tout en
representation canonique afin qu'un meme produit scanne deux fois tombe sur la
meme fiche, y compris quand le lecteur envoie un UPC-A sur 12 chiffres la ou
Open Food Facts attend un EAN-13 sur 13.
"""

from __future__ import annotations

import re

__all__ = ["normalize", "is_gtin", "checksum_ok", "describe"]

_NON_PRINTABLE = re.compile(r"[\x00-\x1f\x7f]")


def _gtin_check_digit(body: str) -> int:
    """Chiffre de controle GS1 (EAN-8/12/13/14) calcule sur le corps du code."""
    total = 0
    # Les poids alternent 3/1 en partant de la droite du corps.
    for i, ch in enumerate(reversed(body)):
        total += int(ch) * (3 if i % 2 == 0 else 1)
    return (10 - total % 10) % 10


def checksum_ok(code: str) -> bool:
    """Verifie la cle de controle d'un GTIN. False si longueur non GS1."""
    if not code.isdigit() or len(code) not in (8, 12, 13, 14):
        return False
    return _gtin_check_digit(code[:-1]) == int(code[-1])


def is_gtin(code: str) -> bool:
    return checksum_ok(code)


def normalize(raw: str) -> str:
    """Nettoie une lecture brute et convertit l'UPC-A en EAN-13.

    Retourne une chaine vide si la lecture ne contient rien d'exploitable.
    """
    if raw is None:
        return ""
    code = _NON_PRINTABLE.sub("", raw).strip()
    if not code:
        return ""

    # Certains wedges ajoutent un prefixe/suffixe d'identification de symbologie.
    code = code.strip().upper()

    if code.isdigit():
        # UPC-A (12) -> EAN-13 par ajout du zero de tete : c'est la meme cle.
        if len(code) == 12 and checksum_ok(code):
            return "0" + code
        # EAN-13 valide, EAN-8 valide, GTIN-14 valide : tel quel.
        if checksum_ok(code):
            return code
        # Zeros de tete parasites sur un EAN-13 ("00" + 13 chiffres).
        stripped = code.lstrip("0")
        if len(stripped) in (8, 12, 13) and checksum_ok(stripped):
            return normalize(stripped)
    return code


def describe(code: str) -> str:
    """Libelle de la symbologie, pour les logs et le dashboard."""
    if not code:
        return "vide"
    if code.isdigit():
        if len(code) == 13 and checksum_ok(code):
            return "EAN-13"
        if len(code) == 8 and checksum_ok(code):
            return "EAN-8"
        if len(code) == 14 and checksum_ok(code):
            return "GTIN-14"
        return f"numerique-{len(code)}"
    return f"alphanum-{len(code)}"
