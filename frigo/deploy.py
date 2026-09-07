"""Deploiement du client sur le terminal.

Le Skorpio n'a pas de reseau utilisable et Linux n'a pas ActiveSync : la carte
memoire est donc la voie de transfert. Ce module prepare l'arborescence exacte
a copier, avec les reglages deja renseignes, et verifie l'integrite de la copie.

L'emplacement d'installation n'est pas anodin : le programme ecrit son tampon
a cote de son executable, donc il doit finir en memoire interne du terminal
(\\Program Files\\SkorpioFrigo). Lance depuis la carte, le tampon vivrait sur un
support amovible -- et partirait avec la carte.
"""

from __future__ import annotations

import hashlib
import logging
import os
import shutil
import socket
import subprocess
from pathlib import Path

__all__ = ["Payload", "find_removable_mounts", "lan_ip", "build_payload",
           "push_to_card", "stage_payload", "serve_payload"]

log = logging.getLogger("frigo.deploy")

INSTALL_DIR = "\\Program Files\\SkorpioFrigo"
CARD_SUBDIR = "SkorpioFrigo"


class Payload:
    """Ce qui doit atterrir sur la carte memoire."""

    def __init__(self, exe: Path, ini: str, readme: str, extras: list[Path]) -> None:
        self.exe = exe
        self.ini = ini
        self.readme = readme
        self.extras = extras


def lan_ip() -> str:
    """IP du PC sur le reseau local, pour pre-remplir le transport TCP.

    On ouvre une socket UDP vers une adresse externe sans rien emettre : c'est
    le seul moyen portable de savoir quelle interface le noyau choisirait.
    """
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
            s.settimeout(0.2)
            s.connect(("192.0.2.1", 9))          # reseau de documentation, RFC 5737
            return s.getsockname()[0]
    except OSError:
        return "192.168.1.10"


def find_removable_mounts() -> list[Path]:
    """Points de montage amovibles susceptibles d'etre la carte du terminal."""
    candidates: list[Path] = []
    user = os.environ.get("USER") or os.environ.get("LOGNAME") or ""
    for base in (f"/run/media/{user}", f"/media/{user}", "/media", "/mnt"):
        root = Path(base)
        if not root.is_dir():
            continue
        try:
            for child in sorted(root.iterdir()):
                if child.is_dir() and os.access(child, os.W_OK):
                    candidates.append(child)
        except OSError:
            continue

    # On ne garde que ce qui est reellement amovible d'apres le noyau.
    removable: list[Path] = []
    for path in candidates:
        if _is_removable(path):
            removable.append(path)
    return removable or candidates


def _is_removable(mount: Path) -> bool:
    try:
        out = subprocess.run(
            ["lsblk", "-no", "RM,MOUNTPOINT"],
            capture_output=True, text=True, timeout=5, check=False,
        ).stdout
    except (OSError, subprocess.SubprocessError):
        return False
    target = str(mount)
    for line in out.splitlines():
        parts = line.split(None, 1)
        if len(parts) == 2 and parts[1].strip() == target:
            return parts[0].strip() == "1"
    return False


def render_ini(
    *,
    device: str,
    transport: str,
    com_port: str,
    baud: int,
    host: str,
    port: int,
    location: str,
) -> str:
    return "\r\n".join([
        "# Reglages du client d'inventaire du frigo",
        "# Modifiables ensuite depuis le terminal : Outils > Reglages",
        "#",
        "# transport = serie (socle USB ou RS-232) ou tcp (Wi-Fi)",
        f"terminal = {device}",
        f"transport = {transport}",
        "#",
        "# Le nom du port USB depend du reglage PC Connection du terminal.",
        "# En cas de doute : Outils > Diagnostic ports teste COM1: a COM9:.",
        f"port_com = {com_port}",
        f"bauds = {baud}",
        "#",
        "# Utilises seulement si transport = tcp",
        f"hote = {host}",
        f"port_tcp = {port}",
        "#",
        f"lieu = {location}",
        "demander_peremption = 1",
        "duree_par_defaut = 0",
        "",
    ])


def render_readme(*, device: str, host: str, port: int) -> str:
    return "\r\n".join([
        "INVENTAIRE DU FRIGO - installation sur le terminal",
        "=" * 50,
        "",
        "A FAIRE SUR LE TERMINAL",
        "",
        "1. Si le .NET Compact Framework 2.0 est absent de",
        "   \\Windows\\ (pas de fichier NETCFv2.*), taper sur le",
        "   fichier .CAB de ce dossier pour l'installer, puis",
        "   redemarrer le terminal.",
        "",
        "2. Avec l'explorateur de fichiers du terminal",
        "   (Demarrer > Programs > File Explorer), copier",
        "   SkorpioFrigo.exe et frigo.ini vers :",
        "",
        f"       {INSTALL_DIR}",
        "",
        "   IMPORTANT : en memoire interne, PAS sur la carte.",
        "   Le programme ecrit son tampon de scans a cote de son",
        "   executable ; sur la carte, les scans partiraient avec",
        "   elle.",
        "",
        "3. Verifier l'emulation clavier du lecteur :",
        "   Control Panel > applet Datalogic / Decoding",
        "     - Wedge / Keyboard emulation ... active",
        "     - Suffixe / Terminator ......... Enter (CR)",
        "     - Prefixe ...................... aucun",
        "",
        "4. Lancer SkorpioFrigo.exe. Verifier dans",
        "   Outils > Reglages que le terminal s'appelle bien",
        f"   {device}, puis scanner un produit.",
        "",
        "5. Pour un lancement automatique, placer un raccourci",
        "   vers le .exe dans \\Windows\\StartUp\\",
        "",
        "A FAIRE SUR LE PC",
        "",
        "   ./inventaire.py serve",
        f"   (tableau de bord sur http://{host}:8077/ ,",
        f"    ecoute TCP sur le port {port})",
        "",
        "Puis sur le terminal : Actions > Envoyer au PC.",
        "",
        "Si la liaison resiste, la voie de secours ne depend",
        "d'aucun pilote :",
        "   terminal : Actions > Exporter sur carte",
        "   PC       : ./inventaire.py importer <fichier>",
        "",
    ])


def build_payload(
    exe: Path,
    *,
    device: str = "SKORPIO1",
    transport: str = "serie",
    com_port: str = "COM1:",
    baud: int = 115200,
    host: str | None = None,
    port: int = 9101,
    location: str = "frigo",
    extras: list[Path] | None = None,
) -> Payload:
    host = host or lan_ip()
    return Payload(
        exe=exe,
        ini=render_ini(device=device, transport=transport, com_port=com_port,
                       baud=baud, host=host, port=port, location=location),
        readme=render_readme(device=device, host=host, port=port),
        extras=list(extras or []),
    )


def _sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def push_to_card(payload: Payload, card: Path) -> dict[str, object]:
    """Copie la charge utile sur la carte et verifie l'integrite.

    Retourne un rapport. Leve OSError si la carte n'est pas ecrivable : mieux
    vaut echouer franchement que laisser croire a une copie reussie.
    """
    target = Path(card) / CARD_SUBDIR
    target.mkdir(parents=True, exist_ok=True)

    exe_target = target / payload.exe.name
    shutil.copy2(payload.exe, exe_target)

    # Verification apres coup : une carte SD fatiguee accepte l'ecriture puis
    # relit autre chose. Le cout est negligeable pour quelques dizaines de Ko.
    os.sync()
    source_digest = _sha256(payload.exe)
    copied_digest = _sha256(exe_target)
    if source_digest != copied_digest:
        raise OSError(
            f"copie corrompue : {exe_target} ne correspond pas a la source "
            f"({copied_digest[:12]} au lieu de {source_digest[:12]}). "
            "Carte defectueuse ou retiree trop tot ?"
        )

    (target / "frigo.ini").write_text(payload.ini, encoding="utf-8", newline="")
    (target / "LISEZMOI.TXT").write_text(payload.readme, encoding="utf-8", newline="")

    copied = [exe_target.name, "frigo.ini", "LISEZMOI.TXT"]
    for extra in payload.extras:
        dest = target / extra.name
        shutil.copy2(extra, dest)
        copied.append(dest.name)

    os.sync()
    return {
        "dossier": str(target),
        "fichiers": copied,
        "empreinte": source_digest[:16],
        "taille_exe": payload.exe.stat().st_size,
        "install_terminal": INSTALL_DIR,
    }


def stage_payload(payload: Payload, directory: Path) -> Path:
    """Ecrit la charge utile dans un dossier ordinaire, sans verification de carte."""
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    shutil.copy2(payload.exe, directory / payload.exe.name)
    (directory / "frigo.ini").write_text(payload.ini, encoding="utf-8", newline="")
    (directory / "LISEZMOI.TXT").write_text(payload.readme, encoding="utf-8", newline="")
    for extra in payload.extras:
        shutil.copy2(extra, directory / extra.name)
    return directory


_INDEX = """<html><head><title>Installation frigo</title></head>
<body bgcolor="#ffffff">
<h3>Client d'inventaire du frigo</h3>
<p>Enregistrez ce fichier, puis copiez-le dans<br>
<b>{install}</b></p>
<ul>
<li><a href="SkorpioFrigo.exe"><b>SkorpioFrigo.exe</b></a> ({taille} octets)</li>
<li><a href="frigo.ini">frigo.ini</a> (reglages deja renseignes)</li>
<li><a href="LISEZMOI.TXT">LISEZMOI.TXT</a></li>
</ul>
<hr>
<p>Dans Internet Explorer : maintenir le doigt sur le lien,<br>
puis <i>Enregistrer la cible sous...</i></p>
</body></html>
"""


def serve_payload(
    payload: Payload,
    *,
    host: str = "0.0.0.0",
    port: int = 8078,
    directory: Path | None = None,
    annonce: str | None = None,
) -> None:
    """Sert la charge utile en HTTP, pour le navigateur du terminal.

    Voie d'installation qui ne demande aucune carte memoire ni aucun pilote :
    si le Wi-Fi du terminal fonctionne, Internet Explorer telecharge le binaire
    directement depuis le PC. La page est volontairement en HTML de 1997 --
    l'Internet Explorer de Windows CE 5.0 ne comprend rien de plus recent.
    """
    import functools
    import tempfile
    from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

    # Emplacement stable plutot qu'un mkdtemp par lancement, qui laissait une
    # copie du binaire dans /tmp a chaque demarrage du serveur.
    tmp = Path(directory) if directory else Path(tempfile.gettempdir()) / "skorpio-payload"
    stage_payload(payload, tmp)
    (tmp / "index.html").write_text(
        _INDEX.format(install=INSTALL_DIR.replace("\\", "\\"),
                      taille=payload.exe.stat().st_size),
        encoding="utf-8",
    )

    class Handler(SimpleHTTPRequestHandler):
        def log_message(self, fmt: str, *a) -> None:
            # Affichage direct : passer par le journal ferait disparaitre ces
            # lignes, le niveau par defaut etant WARNING. Or c'est la seule
            # facon de voir le terminal telecharger.
            #
            # Le User-Agent est indispensable ici : sur une liaison
            # point-a-point, une requete du terminal peut se presenter avec
            # l'adresse locale, si bien que l'IP seule ne dit pas qui telecharge.
            # L'IE de Windows CE s'annonce "Windows CE" ou "MSIE ... WindowsCE".
            ua = self.headers.get("User-Agent", "?") if hasattr(self, "headers") else "?"
            qui = "TERMINAL" if ("windows ce" in ua.lower() or "wince" in ua.lower()) \
                  else "pc/autre"
            print(f"  [{self.address_string()}] [{qui}] {fmt % a}", flush=True)
            print(f"      user-agent: {ua}", flush=True)

        def end_headers(self) -> None:
            # Certaines versions de l'IE de Windows CE refusent de telecharger
            # un fichier servi en octet-stream sans longueur explicite ; le
            # handler de base la fournit, on ajoute juste le non-cache.
            self.send_header("Cache-Control", "no-store")
            super().end_headers()

    handler = functools.partial(Handler, directory=str(tmp))
    srv = ThreadingHTTPServer((host, port), handler)
    log.info("charge utile servie depuis %s", tmp)
    # Sur une liaison PPP par l'USB, l'adresse du PC est celle du bout de
    # tunnel (192.168.131.1), pas celle du reseau local.
    adresse = annonce or lan_ip()
    # flush explicite : ces lignes sont des instructions a suivre, elles ne
    # doivent pas rester coincees dans le tampon jusqu'a l'arret du serveur.
    dire = lambda t: print(t, flush=True)
    dire("  Sur le terminal, ouvrir Internet Explorer et aller a :")
    dire(f"      http://{adresse}:{port}/")
    if annonce is None:
        dire("  (adresse detectee automatiquement ; forcez-la avec --hote si le PC")
        dire("   a plusieurs interfaces, ou en PPP : --hote 192.168.131.1)")
    dire("  Ctrl-C pour arreter.")
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\n  serveur arrete.")
