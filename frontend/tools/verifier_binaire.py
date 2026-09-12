#!/usr/bin/env python3
"""Controle qu'un binaire est bien chargeable par le .NET Compact Framework 2.0.

Un executable peut compiler sans erreur et rester refuse par le terminal, parce
que les en-tetes le presentent comme du .NET de bureau. Ce script verifie les
quatre points qui decident du chargement, en comparant a un vrai binaire du
Compact Framework :

    en-tete CLI  : version de runtime 2.5 (celle du .NET 2.0)
    metadonnees  : chaine "v2.0.50727", celle qu'embarque un binaire CF
    references   : jeton de cle publique 969db8053d3322ac (Compact Framework),
                   et non b77a5c561934e089 (.NET de bureau)
    machine      : IL portable, i386 (le CF recompile a la volee sur ARM)
"""

from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

CF_TOKEN = bytes.fromhex("969db8053d3322ac")        # Compact Framework
DESKTOP_TOKEN = bytes.fromhex("b77a5c561934e089")   # .NET de bureau
ECMA_TOKEN = bytes.fromhex("b03f5f7f11d50a3a")      # cle ECMA (bureau aussi)

ATTENDU_RUNTIME = "2.5"
ATTENDU_METADATA = "v2.0.50727"


def lire(path: Path) -> dict:
    d = path.read_bytes()
    if d[:2] != b"MZ":
        raise ValueError("pas un executable PE")
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    machine = struct.unpack_from("<H", d, pe + 4)[0]
    nsec = struct.unpack_from("<H", d, pe + 6)[0]
    opt_size = struct.unpack_from("<H", d, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from("<H", d, opt)[0]
    cli_rva = struct.unpack_from("<I", d, opt + (208 if magic == 0x10B else 224))[0]
    if not cli_rva:
        raise ValueError("aucun en-tete CLI : ce n'est pas un assembly .NET")
    sec = opt + opt_size

    def off(rva: int) -> int:
        for i in range(nsec):
            b = sec + i * 40
            vsz, va, rsz, praw = struct.unpack_from("<IIII", d, b + 8)
            if va <= rva < va + max(vsz, rsz):
                return praw + (rva - va)
        raise ValueError(f"RVA {rva:#x} hors sections")

    cli = off(cli_rva)
    rt_major, rt_minor = struct.unpack_from("<HH", d, cli + 4)
    cor_flags = struct.unpack_from("<I", d, cli + 16)[0]
    meta = off(struct.unpack_from("<I", d, cli + 8)[0])
    vlen = struct.unpack_from("<I", d, meta + 12)[0]
    version = d[meta + 16:meta + 16 + vlen].rstrip(b"\0").decode("ascii", "replace")

    return {
        "machine": machine,
        "runtime": f"{rt_major}.{rt_minor}",
        "cor_flags": cor_flags,
        "metadata": version,
        "cf_token": d.count(CF_TOKEN),
        "desktop_token": d.count(DESKTOP_TOKEN) + d.count(ECMA_TOKEN),
        "taille": len(d),
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("binaire", type=Path)
    args = ap.parse_args()

    try:
        h = lire(args.binaire)
    except (OSError, ValueError) as exc:
        print(f"  {args.binaire} : {exc}", file=sys.stderr)
        return 2

    verdicts = [
        ("version de runtime CLI", h["runtime"], h["runtime"] == ATTENDU_RUNTIME,
         f"attendu {ATTENDU_RUNTIME}"),
        ("version des metadonnees", h["metadata"], h["metadata"] == ATTENDU_METADATA,
         f"attendu {ATTENDU_METADATA}"),
        ("references Compact Framework", h["cf_token"], h["cf_token"] > 0,
         "au moins une attendue"),
        ("references .NET de bureau", h["desktop_token"], h["desktop_token"] == 0,
         "aucune ne doit apparaitre"),
        ("code machine", hex(h["machine"]), h["machine"] == 0x14C,
         "0x14c attendu (IL portable)"),
        ("IL uniquement", bool(h["cor_flags"] & 1), bool(h["cor_flags"] & 1),
         "le bit ILONLY doit etre pose"),
    ]

    print(f"  {args.binaire}  ({h['taille']} octets)\n")
    echecs = 0
    for label, valeur, ok, attendu in verdicts:
        if not ok:
            echecs += 1
        print(f"  {'OK ' if ok else 'KO '} {label:30} {str(valeur):14} "
              f"{'' if ok else '<- ' + attendu}")
    print()
    if echecs:
        print(f"  {echecs} controle(s) en echec : ce binaire risque d'etre refuse "
              f"par le terminal.")
        return 1
    print("  Binaire conforme a ce qu'attend le Compact Framework 2.0.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
