#!/usr/bin/env python3
"""Extrait les assemblies de reference du .NET Compact Framework 2.0 d'un MSI.

Compiler pour le terminal demande les assemblies de reference du Compact
Framework, que Microsoft ne distribue plus que dans le redistribuable
NETCFSetupv2.msi. Le MSI stocke ses fichiers sous des noms mutiles
(F_MI09D7.1.DLL...), mais chaque assembly porte son vrai nom dans ses
metadonnees : on lit donc le nom la, plutot que de se fier au nom de fichier.

    # SHA1 attendu : 773e6fe43ff5ed31986d75cd916b9c60ce645f22
    curl -fLo NETCFSetupv2.msi \
      'https://web.archive.org/web/20070308000000id_/https://download.microsoft.com/download/0/7/2/0728de3a-fa75-413f-b3b6-8050518cef86/NETCFSetupv2.msi'
    python3 tools/extraire_refs.py NETCFSetupv2.msi

Trois jeux sont produits, et la distinction compte :

* client/refs/          les assemblies de reference, metadonnees seules. Elles
                        decrivent la surface publique documentee du CF 2.0 :
                        compiler contre elles garantit qu'on n'utilise rien
                        d'autre. Leur version de metadonnees est "2.0.0.0".

* client/refs/runtime/  les assemblies reellement installees sur le terminal.
                        Elles portent la version de metadonnees "v2.0.50727",
                        celle qu'un binaire produit par Visual Studio 2008
                        embarque. mcs recopiant cette chaine depuis le corlib
                        reference, c'est contre ce jeu qu'il faut produire le
                        binaire livre.

* client/refs/wince/    les .asmmeta "WindowsCE". Utiles pour verifier la
                        presence d'un type ou d'un membre (tools/verifier_api.py),
                        mais INUTILISABLES comme cibles de compilation : elles
                        amputent les valeurs d'enumeration, si bien qu'un
                        FontStyle.Bold y semble absent.
"""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from nom_assembly import module_name  # noqa: E402

# Le minimum dont le client a besoin, et rien de plus.
NEEDED = ["mscorlib.dll", "System.dll", "System.Drawing.dll", "System.Windows.Forms.dll"]
ASMMETA = ["System.WindowsCE.asmmeta", "System.Drawing.WindowsCE.asmmeta",
           "System.Windows.Forms.WindowsCE.asmmeta"]


def extract_msi(msi: Path, workdir: Path) -> Path:
    """Deballe le MSI. 7z suffit, msitools n'est pas necessaire."""
    raw = workdir / "brut"
    raw.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(
        ["7z", "x", "-y", f"-o{raw}", str(msi)],
        capture_output=True, text=True, check=False,
    )
    if result.returncode != 0:
        raise SystemExit(f"echec de l'extraction du MSI :\n{result.stderr[:400]}")
    return raw


def index_assemblies(raw: Path) -> dict[str, list[Path]]:
    """Associe chaque nom d'assembly reel aux fichiers qui le portent."""
    index: dict[str, list[Path]] = {}
    for path in sorted(raw.iterdir()):
        if not path.is_file():
            continue
        try:
            name = module_name(str(path))
        except Exception:
            continue
        if name:
            index.setdefault(name, []).append(path)
    return index


def pick_reference(candidates: list[Path]) -> Path:
    """Retient les metadonnees de reference : le plus petit fichier.

    Le MSI contient deux jeux sous les memes noms : les metadonnees de
    reference (petites, sans code) et le runtime du terminal (gros, avec l'IL).
    La taille les distingue de facon fiable.
    """
    return min(candidates, key=lambda p: p.stat().st_size)


def pick_runtime(candidates: list[Path]) -> Path:
    """Retient l'assembly du runtime : le plus gros fichier."""
    return max(candidates, key=lambda p: p.stat().st_size)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("msi", type=Path, help="chemin de NETCFSetupv2.msi")
    ap.add_argument("-o", "--sortie", type=Path,
                    default=Path(__file__).resolve().parent.parent / "client" / "refs")
    args = ap.parse_args()

    if not args.msi.is_file():
        raise SystemExit(f"introuvable : {args.msi}")
    if not shutil.which("7z"):
        raise SystemExit("7z est requis (paquet p7zip)")

    with tempfile.TemporaryDirectory(prefix="netcf-") as tmp:
        raw = extract_msi(args.msi, Path(tmp))
        index = index_assemblies(raw)

        refs = args.sortie
        wince = refs / "wince"
        runtime = refs / "runtime"
        for d in (refs, wince, runtime):
            d.mkdir(parents=True, exist_ok=True)

        missing = []
        print(f"assemblies de reference -> {refs}")
        for name in NEEDED:
            if name not in index:
                missing.append(name)
                continue
            src = pick_reference(index[name])
            dst = refs / name
            shutil.copy2(src, dst)
            print(f"  {name:28} {dst.stat().st_size:>8} octets")

        print(f"\nassemblies du runtime terminal -> {runtime}")
        for name in NEEDED:
            if name not in index:
                continue
            src = pick_runtime(index[name])
            dst = runtime / name
            shutil.copy2(src, dst)
            print(f"  {name:28} {dst.stat().st_size:>8} octets")

        print(f"\nsurface d'API Windows CE -> {wince}")
        for name in ASMMETA:
            if name not in index:
                print(f"  {name:44} absent")
                continue
            src = pick_reference(index[name])
            # mcs exige l'extension .dll pour une reference.
            dst = wince / (name + ".dll")
            shutil.copy2(src, dst)
            print(f"  {name:44} {dst.stat().st_size:>8} octets")

        if missing:
            raise SystemExit("\nassemblies manquantes : " + ", ".join(missing))

    print("\ntermine.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
