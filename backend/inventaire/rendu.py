"""Deux ecritures d'une meme reponse : JSON, et `cle=valeur` pour le terminal.

Ecrire un analyseur JSON en C# sous Compact Framework 2.0 est faisable mais
represente environ deux cents lignes de code a risque sur un runtime de 2005,
pour des reponses qui font quelques centaines d'octets. Le format `cle=valeur`
se lit en dix lignes cote terminal et se debogue a l'oeil nu dans un
navigateur. Les deux sorties derivent de la meme structure, il n'y a donc
jamais deux verites.

    ok=1
    produit.nom=Nutella
    lots=2
    lot.0.qte=3
    lot.0.peremption=2026-10-01

Les listes emettent d'abord leur longueur sous leur propre cle, puis un bloc
indexe. Le terminal peut ainsi dimensionner ses tableaux avant de lire.
"""

from __future__ import annotations

import json

__all__ = ["en_json", "en_kv", "aplatir", "echapper"]


def echapper(valeur: str) -> str:
    """Une valeur tient sur une ligne : on neutralise ce qui la couperait."""
    return (valeur.replace("\\", "\\\\")
                  .replace("\r", "\\r")
                  .replace("\n", "\\n"))


def _scalaire(valeur) -> str:
    if valeur is None:
        return ""
    if isinstance(valeur, bool):
        return "1" if valeur else "0"
    if isinstance(valeur, float):
        # repr() donnerait 0.30000000000000004 ; le terminal n'affiche pas
        # plus d'une decimale de toute facon.
        texte = f"{valeur:.3f}".rstrip("0").rstrip(".")
        return texte or "0"
    return echapper(str(valeur))


def aplatir(objet, prefixe: str = "") -> list[tuple[str, str]]:
    """Deroule dictionnaires et listes en une suite de couples plats."""
    lignes: list[tuple[str, str]] = []
    if isinstance(objet, dict):
        for cle, valeur in objet.items():
            lignes.extend(aplatir(valeur, f"{prefixe}.{cle}" if prefixe else str(cle)))
    elif isinstance(objet, (list, tuple)):
        lignes.append((prefixe, str(len(objet))))
        # Le singulier indexe se lit mieux cote C# : lots=2 puis lot.0.qte.
        singulier = prefixe[:-1] if prefixe.endswith("s") and len(prefixe) > 1 else prefixe + ".e"
        for i, element in enumerate(objet):
            lignes.extend(aplatir(element, f"{singulier}.{i}"))
    else:
        lignes.append((prefixe, _scalaire(objet)))
    return lignes


def en_kv(objet) -> bytes:
    corps = "".join(f"{cle}={valeur}\n" for cle, valeur in aplatir(objet) if cle)
    return corps.encode("utf-8")


def en_json(objet) -> bytes:
    return json.dumps(objet, ensure_ascii=False, indent=1).encode("utf-8")


def rendre(objet, format_: str) -> tuple[bytes, str]:
    """Retourne (corps, type MIME) selon le format demande."""
    if format_ == "kv":
        return en_kv(objet), "text/plain; charset=utf-8"
    return en_json(objet), "application/json; charset=utf-8"
