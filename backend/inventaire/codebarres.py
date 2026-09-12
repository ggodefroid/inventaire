"""Cles de controle des codes-barres, et reparation d'un code ampute.

Beaucoup de decodeurs code-barres savent **ne pas transmettre la cle de
controle** : c'est un reglage, souvent actif par defaut sur les terminaux
d'occasion configures pour un logiciel maison qui la recalculait. Le lecteur
decode alors correctement un EAN-13 et n'en envoie que les douze premiers
chiffres. Rien ne le signale : ni erreur, ni bip different. Le code part
incomplet, et aucun catalogue ne le reconnait.

Ce module rattrape le cas quand il est **decidable sans ambiguite**.

Douze chiffres, c'est soit un UPC-A complet, soit un EAN-13 ampute de sa cle.
Les deux se distinguent par le calcul : si les douze chiffres forment un UPC-A
valide, c'en est un ; sinon le douzieme n'est pas une cle, et il s'agit d'un
EAN-13 auquel il manque la sienne. La probabilite qu'un EAN-13 ampute passe par
hasard pour un UPC-A valide est d'un sur dix -- on prefere alors le laisser
tel quel plutot que d'inventer un chiffre.

Le bon reglage reste a faire sur le terminal : voir docs/01, § lecteur.
"""

from __future__ import annotations

__all__ = ["cle", "valide", "reparer", "variantes", "chiffres"]


def chiffres(code: str) -> bool:
    return bool(code) and code.isdigit()


def cle(corps: str) -> str:
    """Cle de controle d'un corps de code EAN / UPC.

    Meme calcul pour EAN-8, UPC-A et EAN-13 : en partant de la droite du
    corps, les rangs alternent les poids 3 et 1.
    """
    total = 0
    for rang, chiffre in enumerate(reversed(corps)):
        total += int(chiffre) * (3 if rang % 2 == 0 else 1)
    return str((10 - total % 10) % 10)


def valide(code: str) -> bool:
    """true si le dernier chiffre est bien la cle des precedents."""
    if not chiffres(code) or len(code) < 2:
        return False
    return cle(code[:-1]) == code[-1]


def reparer(code: str) -> str:
    """Complete un EAN-13 dont le lecteur a retenu la cle. Sinon, rend le code tel quel.

    Volontairement limite aux douze chiffres. Sur sept chiffres, un EAN-8
    ampute et un code interne sont indiscernables : il n'y a aucun moyen de
    trancher, et completer au hasard serait pire que de ne rien faire.
    """
    if not chiffres(code) or len(code) != 12:
        return code
    if valide(code):
        return code                 # UPC-A complet : on n'y touche pas
    return code + cle(code)         # EAN-13 ampute de sa cle


def variantes(code: str, troncature_probable: bool = False) -> list[str]:
    """Formes plausibles du code lu, de la plus probable a la moins probable.

    Douze chiffres qui ne forment pas un UPC-A valide sont forcement un EAN-13
    ampute : le calcul le prouve. Mais l'inverse n'est pas vrai -- un EAN-13
    ampute a une chance sur dix de passer pour un UPC-A valide, et c'est arrive
    en vrai sur 359671035508, qui est en realite 3596710355082.

    Dans ce cas, l'arithmetique ne peut plus trancher : c'est le catalogue qui
    le fait. On lui propose donc les deux lectures, et celle qu'il reconnait
    gagne. `troncature_probable` place la completion en tete quand le serveur a
    deja constate que ce lecteur retient les cles de controle.
    """
    if not chiffres(code):
        return [code]
    n = len(code)
    complet = code + cle(code)

    if n == 12:
        # UPC-A authentique, son equivalent EAN-13 a zero initial, ou EAN-13
        # ampute. L'ordre depend de ce que le calcul et l'experience disent.
        if not valide(code) or troncature_probable:
            return [complet, code, "0" + code]
        return [code, "0" + code, complet]
    if n == 13:
        return [code, code[1:]] if code[0] == "0" else [code]
    if n == 11:
        return [complet, code]          # UPC-A ampute de sa cle
    if n == 7:
        return [complet, code]          # EAN-8 ampute de sa cle
    return [code]
