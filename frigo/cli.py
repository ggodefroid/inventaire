"""Interface en ligne de commande du serveur d'inventaire.

    ./inventaire.py serve            demarre tout (USB + TCP + tableau de bord)
    ./inventaire.py ports            liste les ports serie et diagnostique l'USB
    ./inventaire.py simuler          faux terminal, pour tester sans le Skorpio
    ./inventaire.py importer f.txt   ingere un tampon depose sur carte memoire
    ./inventaire.py pousser          prepare/copie le client pour le terminal
    ./inventaire.py ls               etat du frigo
    ./inventaire.py bientot -j 3     ce qui perime dans les 3 jours
"""

from __future__ import annotations

import argparse
import asyncio
import csv
import io
import json
import logging
import signal
import sys
import threading
from pathlib import Path

from . import __version__
from . import barcode as bc
from .dates import ExpiryError, days_left, format_expiry, parse_expiry
from .db import DEFAULT_DB, Store
from .products import Enricher, resolve
from .protocol import LINE_END, PROTO_VERSION, encode
from .webui import make_server

log = logging.getLogger("frigo")

# ---------------------------------------------------------------- presentation

_TTY = sys.stdout.isatty()


def _c(text: str, code: str) -> str:
    return f"\033[{code}m{text}\033[0m" if _TTY else text


def _urgency_color(expiry: str | None) -> str:
    d = days_left(expiry)
    if d is None:
        return "2;37"
    if d < 0:
        return "1;31"
    if d <= 2:
        return "1;33"
    if d <= 6:
        return "33"
    return "32"


def _print_items(rows) -> None:
    if not rows:
        print(_c("  (rien)", "2;37"))
        return
    widths = [4, 34, 14, 10, 9, 8]
    header = ("id", "produit", "code-barres", "peremption", "etat", "lieu")
    print("  " + _c("  ".join(h.upper().ljust(w) for h, w in zip(header, widths)), "2;37"))
    for r in rows:
        name = (r["name"] or "?") + (f" x{r['qty']}" if r["qty"] > 1 else "")
        d = days_left(r["expiry"])
        state = "sans date" if d is None else (f"perime {-d}j" if d < 0 else f"J-{d}")
        cells = [
            str(r["id"]).ljust(widths[0]),
            name[: widths[1]].ljust(widths[1]),
            r["barcode"][: widths[2]].ljust(widths[2]),
            format_expiry(r["expiry"]).ljust(widths[3]),
            _c(state.ljust(widths[4]), _urgency_color(r["expiry"])),
            r["location"][: widths[5]],
        ]
        print("  " + "  ".join(cells))


# ------------------------------------------------------------------ sous-commandes


def cmd_serve(args, store: Store) -> int:
    enricher = None
    if not args.hors_ligne:
        enricher = Enricher(store, online=True)
        enricher.start()

    stop = threading.Event()
    threads: list[threading.Thread] = []
    links = []

    if not args.sans_usb:
        from .transports.serial_link import SerialLink, find_datalogic_port

        port = args.port_serie or find_datalogic_port()
        if not port:
            log.warning(
                "aucun port serie detecte pour l'instant ; le serveur attendra "
                "que le terminal soit pose sur son socle "
                "(voir `inventaire.py ports` en cas de doute)"
            )
        link = SerialLink(
            store, port,
            baudrate=args.bauds,
            online=not args.hors_ligne,
            enricher=enricher,
            default_location=args.lieu,
        )
        links.append(link)
        t = threading.Thread(target=link.run_forever, name="serie", daemon=True)
        t.start()
        threads.append(t)

    if not args.sans_tcp:
        from .transports.tcp_link import serve_tcp

        def _tcp() -> None:
            asyncio.run(serve_tcp(
                store, host=args.hote_tcp, port=args.port_tcp,
                online=not args.hors_ligne, enricher=enricher,
                default_location=args.lieu,
            ))

        t = threading.Thread(target=_tcp, name="tcp", daemon=True)
        t.start()
        threads.append(t)

    httpd = None
    if not args.sans_web:
        httpd = make_server(store, host=args.hote_web, port=args.port_web,
                            enricher=enricher, online=not args.hors_ligne)
        print(_c(f"  tableau de bord : http://{args.hote_web}:{args.port_web}/", "1;36"))

    if not args.sans_tcp:
        print(_c(f"  ecoute TCP      : {args.hote_tcp}:{args.port_tcp}", "36"))
    if not args.sans_usb:
        print(_c(f"  liaison serie   : {args.port_serie or 'auto-detection'} "
                 f"@ {args.bauds} bauds", "36"))
    print(_c(f"  base            : {store.path}", "2;37"))
    print(_c("  Ctrl-C pour arreter\n", "2;37"))

    def _shutdown(signum, frame) -> None:
        stop.set()
        for link in links:
            link.stop()
        if enricher:
            enricher.stop()
        if httpd:
            threading.Thread(target=httpd.shutdown, daemon=True).start()

    signal.signal(signal.SIGINT, _shutdown)
    signal.signal(signal.SIGTERM, _shutdown)

    try:
        if httpd:
            httpd.serve_forever()
        else:
            while not stop.is_set():
                stop.wait(1.0)
    finally:
        print("\n" + _c("serveur arrete.", "2;37"))
    return 0


def cmd_ports(args, store: Store) -> int:
    from .transports.serial_link import DATALOGIC_VID, list_candidate_ports

    ports = list_candidate_ports(include_phantom=args.tout)
    print(_c("Ports serie visibles", "1"))
    if ports:
        for p in ports:
            tag = _c("  <- Datalogic", "1;32") if p["datalogic"] else ""
            print(f"  {p['device']:16} {p['vid']:8} {p['pid']:8} {p['description']}{tag}")
    else:
        print(_c("  aucun port serie reel "
                 "(--tout pour voir les /dev/ttyS* de la carte mere)", "2;37"))

    print()
    print(_c("Peripheriques USB Datalogic", "1"))
    found = False
    try:
        from pathlib import Path

        for d in sorted(Path("/sys/bus/usb/devices").glob("*/idVendor")):
            if d.read_text().strip().lower() != f"{DATALOGIC_VID:04x}":
                continue
            found = True
            dev = d.parent
            pid = (dev / "idProduct").read_text().strip()
            name = (dev / "product").read_text().strip() if (dev / "product").exists() else "?"
            drivers = []
            for iface in sorted(dev.glob(f"{dev.name}:*")):
                drv = (iface / "driver")
                drivers.append(drv.resolve().name if drv.exists() else "<aucun>")
            print(f"  {dev.name}: {name} (080c:{pid}) pilote(s) : {', '.join(drivers) or '-'}")
            if all(d == "<aucun>" for d in drivers):
                new_id = Path("/sys/bus/usb-serial/drivers/ipaq/new_id")
                print(_c(
                    "\n  Aucun pilote n'est attache : le port serie n'existe donc pas encore.\n"
                    "  Sur ce modele (profil bulk vendor-specific de Windows CE), le module\n"
                    "  noyau `ipaq` sait exposer la liaison, mais il ne connait pas cet\n"
                    "  identifiant : il faut le lui declarer.\n", "33"))
                if not new_id.exists():
                    print(_c("      sudo modprobe ipaq", "1;36"))
                print(_c(f"      echo 080c {pid} | sudo tee {new_id}\n", "1;36"))
                print(_c(
                    "  (les parametres de module vendor=/product= des anciennes\n"
                    "   documentations n'existent plus : voir `modinfo -p ipaq`)\n\n"
                    "  Puis relancez `inventaire.py ports`. Pour rendre cela permanent,\n"
                    "  voir tools/skorpio-udev.rules et docs/02-liaison-usb.md", "33"))
    except OSError:
        pass
    if not found:
        print(_c("  aucun (terminal debranche ou eteint ?)", "2;37"))
    return 0


def cmd_import(args, store: Store) -> int:
    from .transports.fileimport import import_path

    enricher = None
    if not args.hors_ligne:
        enricher = Enricher(store, online=True)
    total_ok = 0
    for path in args.fichiers:
        session = import_path(
            store, path,
            device=args.terminal,
            online=not args.hors_ligne,
            enricher=enricher,
            default_location=args.lieu,
            archive=args.archiver,
        )
        print(f"  {path} : {session.stats}")
        total_ok += session.stats.ok
    if enricher and total_ok:
        print(_c(f"  recherche des libelles pour {total_ok} nouvel(s) article(s)...", "2;37"))
        enricher._pass()
    return 0


def cmd_push(args, store: Store) -> int:
    """Prepare la charge utile a copier sur la carte memoire du terminal."""
    from .deploy import build_payload, find_removable_mounts, lan_ip, push_to_card

    exe = Path(args.exe) if args.exe else None
    if exe is None:
        for candidate in (Path("client/bin/SkorpioFrigo.exe"),
                          Path("client/SkorpioFrigo/bin/Release/SkorpioFrigo.exe"),
                          Path("client/SkorpioFrigo/bin/Debug/SkorpioFrigo.exe")):
            if candidate.is_file():
                exe = candidate
                break
    if exe is None or not exe.is_file():
        print(_c("  Aucun executable client trouve.", "1;31"))
        print("""
  Le client doit d'abord etre compile. Deux voies :

    Depuis Linux (assemblies de reference deja extraites dans client/refs/) :
        sudo pacman -S mono
        ./client/build-linux.sh

    Depuis Windows :
        ouvrir client/SkorpioFrigo/SkorpioFrigo.csproj avec Visual Studio 2008
        (voir client/README.md)

  Puis relancez cette commande, ou indiquez le binaire avec --exe.
""")
        return 2

    if args.http:
        from .deploy import serve_payload
        payload = build_payload(
            exe, device=args.terminal, transport=args.transport,
            com_port=args.port_com, baud=args.bauds, host=args.hote or lan_ip(),
            port=args.port_tcp, location=args.lieu,
            extras=[Path(p) for p in (args.avec or []) if Path(p).is_file()],
        )
        print(_c("\n  Installation par le navigateur du terminal "
                 "(aucune carte requise)", "1"), flush=True)
        serve_payload(payload, port=args.port_http, annonce=args.hote)
        return 0

    if args.cible:
        cards = [Path(args.cible)]
    else:
        cards = find_removable_mounts()
    if not cards:
        print(_c("  Aucun support amovible monte.", "1;33"))
        print("""
  Inserez la carte mini-SD ou Compact Flash du terminal, puis relancez.
  Sinon, preparez le dossier ailleurs et copiez-le vous-meme :

        ./inventaire.py pousser ~/carte-skorpio
""")
        return 2

    card = cards[0]
    if len(cards) > 1:
        print(_c(f"  plusieurs supports detectes, on prend {card}", "2;37"))
        for other in cards[1:]:
            print(_c(f"    (autres : {other})", "2;37"))

    extras = [Path(p) for p in (args.avec or []) if Path(p).is_file()]
    payload = build_payload(
        exe,
        device=args.terminal,
        transport=args.transport,
        com_port=args.port_com,
        baud=args.bauds,
        host=args.hote or lan_ip(),
        port=args.port_tcp,
        location=args.lieu,
        extras=extras,
    )

    try:
        report = push_to_card(payload, card)
    except OSError as exc:
        print(_c(f"  echec : {exc}", "1;31"))
        return 1

    print()
    print(_c(f"  Charge utile ecrite dans {report['dossier']}", "1;32"))
    for name in report["fichiers"]:
        print(f"    {name}")
    print()
    print(_c(f"  empreinte exe : {report['empreinte']}  "
             f"({report['taille_exe']} octets, verifiee apres copie)", "2;37"))
    print(f"""
  Sur le terminal :
    1. inserer la carte, ouvrir File Explorer
    2. copier SkorpioFrigo.exe et frigo.ini vers
         {report['install_terminal']}
       (memoire interne, PAS la carte : le tampon vit a cote du .exe)
    3. lancer SkorpioFrigo.exe

  Le detail est dans LISEZMOI.TXT, sur la carte.
""")
    return 0


def cmd_ls(args, store: Store) -> int:
    rows = store.inventory(location=args.lieu_filtre, order=args.tri)
    s = store.stats()
    print(_c(f"\n  {s['presents']} article(s) present(s) "
             f"- {s['perimes']} perime(s) - {s['sans_date']} sans date\n", "1"))
    _print_items(rows)
    print()
    return 0


def cmd_soon(args, store: Store) -> int:
    rows = store.expiring(args.jours)
    print(_c(f"\n  Peremption sous {args.jours} jour(s)\n", "1"))
    _print_items(rows)
    print()
    return 0


def cmd_add(args, store: Store) -> int:
    code = bc.normalize(args.code)
    if not code:
        print("code-barres vide", file=sys.stderr)
        return 2
    try:
        expiry = parse_expiry(args.peremption)
    except ExpiryError as exc:
        print(f"date refusee : {exc}", file=sys.stderr)
        return 2
    verdict, item_id = store.add_item(
        code, expiry=expiry.isoformat() if expiry else None,
        qty=args.quantite, location=args.lieu, note=args.note, device="cli",
    )
    label = resolve(store, code, online=not args.hors_ligne) or "(produit inconnu)"
    print(f"  {verdict} #{item_id} : {label} - {code} - {format_expiry(expiry)}")
    return 0


def cmd_consume(args, store: Store) -> int:
    if args.id:
        ok = store.consume(args.id)
        print("  sorti" if ok else "  introuvable ou deja sorti")
        return 0 if ok else 1
    code = bc.normalize(args.code or "")
    item_id = store.consume_barcode(code)
    if item_id is None:
        print(f"  aucun article present pour {code}")
        return 1
    print(f"  sorti #{item_id} ({store.product_label(code) or code})")
    return 0


def cmd_name(args, store: Store) -> int:
    code = bc.normalize(args.code)
    if args.libelle:
        store.product_upsert(code, name=args.libelle, source="manuel")
        print(f"  {code} = {args.libelle}")
    else:
        label = resolve(store, code, online=True, refresh=args.rafraichir)
        print(f"  {code} = {label or '(introuvable sur Open Food Facts)'}")
    return 0


def cmd_export(args, store: Store) -> int:
    rows = [dict(r) for r in store.inventory(include_consumed=args.tout)]
    for r in rows:
        r["jours_restants"] = days_left(r["expiry"])
    if args.format == "json":
        text = json.dumps(rows, ensure_ascii=False, indent=2)
    else:
        buf = io.StringIO()
        cols = ["id", "barcode", "name", "brand", "expiry", "jours_restants",
                "qty", "location", "device", "scanned_at", "added_at", "consumed_at"]
        w = csv.DictWriter(buf, fieldnames=cols, extrasaction="ignore", delimiter=";")
        w.writeheader()
        w.writerows(rows)
        text = buf.getvalue()
    if args.sortie:
        with open(args.sortie, "w", encoding="utf-8") as fh:
            fh.write(text)
        print(f"  {len(rows)} ligne(s) -> {args.sortie}")
    else:
        print(text)
    return 0


def cmd_stats(args, store: Store) -> int:
    for key, value in store.stats().items():
        print(f"  {key:12} {value}")
    return 0


def cmd_simulate(args, store: Store) -> int:
    """Faux terminal Skorpio : valide serveur, protocole et base sans materiel."""
    import socket

    seq_file = store.path.parent / f".seq-{args.terminal}"
    seq = int(seq_file.read_text()) if seq_file.exists() else 0

    print(_c(f"\n  Simulateur de terminal {args.terminal} "
             f"-> {args.hote}:{args.port_tcp}", "1"))
    print(_c("  Entrez un code-barres, puis une date (vide = inconnue, "
             "'+7' = dans 7 jours).\n  Ligne vide pour quitter.\n", "2;37"))

    with socket.create_connection((args.hote, args.port_tcp), timeout=10) as sock:
        fh = sock.makefile("rwb")

        def send(line: str) -> str:
            fh.write((line + LINE_END).encode())
            fh.flush()
            reply = fh.readline().decode("utf-8", "replace").strip()
            return reply

        print("  " + _c(send(encode("HELLO", args.terminal, PROTO_VERSION, __version__)), "36"))
        try:
            while True:
                code = input(_c("  code-barres > ", "1;36")).strip()
                if not code:
                    break
                info = send(encode("LOOK", code))
                parts = info.split("|")
                if len(parts) > 2 and parts[0] == "INFO":
                    print(_c(f"    produit : {parts[2]}", "2;37"))
                date = input(_c("  peremption  > ", "1;36")).strip()
                seq += 1
                reply = send(encode("SCAN", seq, code, date, 1, args.lieu,
                                    __import__("datetime").datetime.now()
                                    .replace(microsecond=0).isoformat(), ""))
                colour = "32" if reply.startswith("OK") else (
                    "33" if reply.startswith("DUP") else "31")
                print("    " + _c(reply, colour) + "\n")
                seq_file.write_text(str(seq))
        except (EOFError, KeyboardInterrupt):
            print()
        print("  " + _c(send(encode("BYE", seq)), "36"))
    return 0


# ------------------------------------------------------------------- assemblage


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="inventaire.py",
        description="Inventaire du frigo alimente par un terminal Datalogic Skorpio.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    p.add_argument("--db", default=str(DEFAULT_DB), help="fichier SQLite (defaut: %(default)s)")
    p.add_argument("--hors-ligne", action="store_true",
                   help="ne jamais interroger Open Food Facts")
    p.add_argument("-v", "--verbeux", action="count", default=0)
    p.add_argument("--version", action="version", version=f"%(prog)s {__version__}")
    sub = p.add_subparsers(dest="commande", required=True, metavar="<commande>")

    sp = sub.add_parser("serve", help="demarre le serveur (USB + TCP + tableau de bord)")
    sp.add_argument("--port-serie", help="ex. /dev/ttyUSB0 (defaut: auto-detection)")
    sp.add_argument("--bauds", type=int, default=115200)
    sp.add_argument("--port-tcp", type=int, default=9101)
    sp.add_argument("--hote-tcp", default="0.0.0.0")
    sp.add_argument("--port-web", type=int, default=8077)
    sp.add_argument("--hote-web", default="127.0.0.1")
    sp.add_argument("--lieu", default="frigo", help="lieu par defaut des scans")
    sp.add_argument("--sans-usb", action="store_true")
    sp.add_argument("--sans-tcp", action="store_true")
    sp.add_argument("--sans-web", action="store_true")
    sp.set_defaults(func=cmd_serve)

    sp = sub.add_parser("ports", help="liste les ports serie et diagnostique l'USB")
    sp.add_argument("--tout", action="store_true",
                    help="inclure les /dev/ttyS* fantomes de la carte mere")
    sp.set_defaults(func=cmd_ports)

    sp = sub.add_parser("importer", help="ingere un ou plusieurs fichiers de tampon")
    sp.add_argument("fichiers", nargs="+")
    sp.add_argument("--terminal", default="SKORPIO")
    sp.add_argument("--lieu", default="frigo")
    sp.add_argument("--archiver", action="store_true",
                    help="renomme le fichier en .importe apres succes")
    sp.set_defaults(func=cmd_import)

    sp = sub.add_parser("pousser", help="prepare le client pour le terminal")
    sp.add_argument("cible", nargs="?",
                    help="dossier ou carte de destination (defaut: support amovible detecte)")
    sp.add_argument("--exe", help="binaire du client (defaut: client/bin/SkorpioFrigo.exe)")
    sp.add_argument("--terminal", default="SKORPIO1")
    sp.add_argument("--transport", choices=("serie", "tcp"), default="serie")
    sp.add_argument("--port-com", default="COM1:")
    sp.add_argument("--bauds", type=int, default=115200)
    sp.add_argument("--hote", help="IP du PC (defaut: detectee)")
    sp.add_argument("--port-tcp", type=int, default=9101)
    sp.add_argument("--lieu", default="frigo")
    sp.add_argument("--avec", nargs="*",
                    help="fichiers a joindre, ex. le CAB du .NET CF 2.0")
    sp.add_argument("--http", action="store_true",
                    help="servir la charge utile en HTTP pour le navigateur du terminal")
    sp.add_argument("--port-http", type=int, default=8078)
    sp.set_defaults(func=cmd_push)

    sp = sub.add_parser("ls", help="etat du frigo")
    sp.add_argument("--lieu-filtre", help="ne montrer qu'un lieu")
    sp.add_argument("--tri", choices=("expiry", "added", "name"), default="expiry")
    sp.set_defaults(func=cmd_ls)

    sp = sub.add_parser("bientot", help="ce qui perime bientot")
    sp.add_argument("-j", "--jours", type=int, default=3)
    sp.set_defaults(func=cmd_soon)

    sp = sub.add_parser("ajouter", help="ajoute un article a la main")
    sp.add_argument("code")
    sp.add_argument("peremption", nargs="?", default="")
    sp.add_argument("-q", "--quantite", type=int, default=1)
    sp.add_argument("--lieu", default="frigo")
    sp.add_argument("--note")
    sp.set_defaults(func=cmd_add)

    sp = sub.add_parser("consommer", help="sort un article du frigo (le plus urgent d'abord)")
    sp.add_argument("code", nargs="?")
    sp.add_argument("--id", type=int)
    sp.set_defaults(func=cmd_consume)

    sp = sub.add_parser("nommer", help="libelle un produit (Open Food Facts ou a la main)")
    sp.add_argument("code")
    sp.add_argument("libelle", nargs="?")
    sp.add_argument("--rafraichir", action="store_true")
    sp.set_defaults(func=cmd_name)

    sp = sub.add_parser("exporter", help="exporte l'inventaire")
    sp.add_argument("--format", choices=("csv", "json"), default="csv")
    sp.add_argument("-o", "--sortie")
    sp.add_argument("--tout", action="store_true", help="inclure les articles sortis")
    sp.set_defaults(func=cmd_export)

    sp = sub.add_parser("stats", help="compteurs de la base")
    sp.set_defaults(func=cmd_stats)

    sp = sub.add_parser("simuler", help="faux terminal, pour tester sans le Skorpio")
    sp.add_argument("--hote", default="127.0.0.1")
    sp.add_argument("--port-tcp", type=int, default=9101)
    sp.add_argument("--terminal", default="SIMU")
    sp.add_argument("--lieu", default="frigo")
    sp.set_defaults(func=cmd_simulate)

    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    level = logging.WARNING - min(args.verbeux, 2) * 10
    logging.basicConfig(
        level=level,
        format="%(asctime)s %(levelname)-7s %(name)-16s %(message)s",
        datefmt="%H:%M:%S",
    )
    store = Store(args.db)
    try:
        return args.func(args, store)
    except KeyboardInterrupt:
        return 130
