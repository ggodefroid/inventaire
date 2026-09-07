#!/usr/bin/env python3
"""Verifie que les membres .NET utilises par le client existent bien sous CF 2.0.

Le Compact Framework est un sous-ensemble du .NET de bureau, et Windows CE
generique est lui-meme un sous-ensemble du Pocket PC. Ecrire du code contre la
documentation du .NET complet mene donc a des surprises a la compilation, voire
a l'execution.

Le tas de chaines (#Strings) d'un assembly contient tous les noms de types et de
membres qu'il declare. Chercher un nom dedans donne un test negatif sur : si
"WordWrap" n'y figure pas, aucun type de cet assembly n'expose cette propriete.
C'est grossier -- un nom present ne dit pas sur quel type il se trouve -- mais
c'est suffisant pour trancher les cas douteux avant de sortir le compilateur,
et ca ne demande aucun outillage.
"""

from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

# Membres dont la presence sous CF 2.0 / Windows CE ne va pas de soi.
# Chaque entree : (nom, assembly attendu, note).
CHECKS = [
    ("MainMenu",           "System.Windows.Forms", "barre de menus, idiome CE"),
    ("MenuItem",           "System.Windows.Forms", "entree de menu"),
    ("KeyPreview",         "System.Windows.Forms", "capture clavier au niveau du formulaire"),
    ("Multiline",          "System.Windows.Forms", "TextBox multiligne"),
    ("ScrollBars",         "System.Windows.Forms", "barres de defilement du TextBox"),
    ("WordWrap",           "System.Windows.Forms", "defilement horizontal du diagnostic"),
    ("AutoScroll",         "System.Windows.Forms", "reglages defilants en paysage"),
    ("DoEvents",           "System.Windows.Forms", "pompe de messages pendant l'envoi"),
    ("ShowDialog",         "System.Windows.Forms", "formulaires modaux"),
    ("DialogResult",       "System.Windows.Forms", "resultat de dialogue"),
    ("ComboBox",           "System.Windows.Forms", "liste deroulante des reglages"),
    ("CheckBox",           "System.Windows.Forms", "case a cocher"),
    ("FormWindowState",    "System.Windows.Forms", "maximisation"),
    ("SelectionStart",     "System.Windows.Forms", "position du curseur"),
    ("ContentAlignment",   "System.Drawing",       "alignement des libelles"),
    ("FontStyle",          "System.Drawing",       "gras"),
    ("SerialPort",         "System",               "liaison serie du socle"),
    ("DiscardInBuffer",    "System",               "purge du tampon serie"),
    ("Handshake",          "System",               "controle de flux"),
    ("ReadByte",           "System",               "lecture octet par octet"),
    ("Poll",               "System",               "attente sur socket avec echeance"),
    ("SelectMode",         "System",               "mode d'attente socket"),
    ("GetHostEntry",       "System",               "resolution DNS"),
    ("GetModules",         "mscorlib",             "chemin de l'executable sous CE"),
    ("DaysInMonth",        "mscorlib",             "validation des dates"),
    ("MTAThreadAttribute", "mscorlib",             "modele de threading du Main"),
    ("TryGetValue",        "mscorlib",             "Dictionary des reglages"),
    ("RemoveRange",        "mscorlib",             "tampon de lecture TCP"),
]


def strings_heap(path: Path) -> set[str]:
    """Tous les noms declares par un assembly, lus dans son tas #Strings."""
    d = path.read_bytes()
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    nsec = struct.unpack_from("<H", d, pe + 6)[0]
    opt_size = struct.unpack_from("<H", d, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from("<H", d, opt)[0]
    cli_rva = struct.unpack_from("<I", d, opt + (208 if magic == 0x10B else 224))[0]
    sec = opt + opt_size

    def off(rva: int) -> int:
        for i in range(nsec):
            b = sec + i * 40
            vsz, va, rsz, praw = struct.unpack_from("<IIII", d, b + 8)
            if va <= rva < va + max(vsz, rsz):
                return praw + (rva - va)
        raise ValueError(f"RVA {rva:#x} hors sections")

    cli = off(cli_rva)
    meta_rva = struct.unpack_from("<I", d, cli + 8)[0]
    root = off(meta_rva)
    ver_len = (struct.unpack_from("<I", d, root + 12)[0] + 3) & ~3
    p = root + 16 + ver_len + 2
    nstreams = struct.unpack_from("<H", d, p)[0]
    p += 2
    for _ in range(nstreams):
        s_off, s_size = struct.unpack_from("<II", d, p)
        p += 8
        end = d.index(b"\0", p)
        name = d[p:end].decode("ascii")
        p += (end - p + 4) & ~3
        if name == "#Strings":
            blob = d[root + s_off:root + s_off + s_size]
            return {s.decode("utf-8", "replace") for s in blob.split(b"\0") if s}
    return set()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--refs", type=Path,
                    default=Path(__file__).resolve().parent.parent / "client" / "refs")
    args = ap.parse_args()

    base = {
        "mscorlib": args.refs / "mscorlib.dll",
        "System": args.refs / "System.dll",
        "System.Drawing": args.refs / "System.Drawing.dll",
        "System.Windows.Forms": args.refs / "System.Windows.Forms.dll",
    }
    wince = {
        "System": args.refs / "wince" / "System.WindowsCE.asmmeta.dll",
        "System.Drawing": args.refs / "wince" / "System.Drawing.WindowsCE.asmmeta.dll",
        "System.Windows.Forms": args.refs / "wince" / "System.Windows.Forms.WindowsCE.asmmeta.dll",
    }

    heaps: dict[str, set[str]] = {}
    for label, path in base.items():
        if not path.is_file():
            raise SystemExit(f"assembly de reference absente : {path}")
        heaps[label] = strings_heap(path)
    wheaps: dict[str, set[str]] = {}
    for label, path in wince.items():
        if path.is_file():
            wheaps[label] = strings_heap(path)

    print(f"{'membre':22} {'assembly':22} {'CF 2.0':8} {'CE generique':13} note")
    print("-" * 96)
    problems = 0
    for name, asm, note in CHECKS:
        in_cf = name in heaps.get(asm, set())
        if asm in wheaps:
            in_ce = "oui" if name in wheaps[asm] else "NON"
        else:
            in_ce = "(idem)"          # mscorlib n'a pas de variante par plateforme
        expected_absent = "suppose absent" in note
        ok = (not in_cf) if expected_absent else in_cf
        if not ok or in_ce == "NON":
            problems += 1
        mark = " " if ok and in_ce != "NON" else "!"
        print(f"{mark}{name:21} {asm:22} {'oui' if in_cf else 'NON':8} {in_ce:13} {note}")

    print()
    if problems:
        print(f"{problems} point(s) a regarder de pres (ligne prefixee de !).")
    else:
        print("Toutes les hypotheses d'API sont confirmees.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
