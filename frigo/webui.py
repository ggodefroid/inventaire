"""Tableau de bord local : ce qui est dans le frigo, et ce qui va perimer.

Pas de dependance externe et pas de CDN : le serveur tourne sur le PC de la
maison, la page doit s'ouvrir meme sans Internet. Tout est en une page, avec un
code couleur d'urgence, et un affichage qui reste lisible sur telephone.
"""

from __future__ import annotations

import datetime as dt
import html
import json
import logging
import socketserver
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from . import barcode as bc
from .dates import ExpiryError, days_left, format_expiry, parse_expiry
from .db import Store
from .products import Enricher, resolve

__all__ = ["make_server", "serve_web"]

log = logging.getLogger("frigo.web")

CSS = """
:root{--bg:#f6f6f4;--card:#fff;--fg:#1a1a18;--muted:#6b6b66;--line:#e2e2dd;
      --ok:#2f7d4f;--warn:#b06a00;--soon:#8a6d00;--dead:#b3261e;--accent:#2b5f9e}
@media (prefers-color-scheme:dark){
  :root{--bg:#16171a;--card:#1e2024;--fg:#e8e8e6;--muted:#9a9a95;--line:#31343a;
        --ok:#5fbf85;--warn:#e0a44a;--soon:#d4c15f;--dead:#f2837a;--accent:#7aa8e0}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);
     font:15px/1.5 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}
header{padding:18px 20px 10px;display:flex;flex-wrap:wrap;gap:14px;align-items:baseline}
h1{font-size:19px;margin:0;letter-spacing:-.01em}
.sub{color:var(--muted);font-size:13px}
main{padding:0 20px 40px;max-width:1080px;margin:0 auto}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:10px;margin:14px 0 22px}
.tile{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 14px}
.tile b{display:block;font-size:24px;font-weight:600;letter-spacing:-.02em}
.tile span{color:var(--muted);font-size:12px;text-transform:uppercase;letter-spacing:.04em}
h2{font-size:14px;text-transform:uppercase;letter-spacing:.06em;color:var(--muted);
   margin:26px 0 8px;font-weight:600}
.wrap{overflow-x:auto;background:var(--card);border:1px solid var(--line);border-radius:10px}
table{border-collapse:collapse;width:100%;font-size:14px}
th{text-align:left;font-size:11px;text-transform:uppercase;letter-spacing:.05em;
   color:var(--muted);padding:9px 12px;border-bottom:1px solid var(--line);white-space:nowrap}
td{padding:9px 12px;border-bottom:1px solid var(--line);vertical-align:middle}
tr:last-child td{border-bottom:0}
.code{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:12px;color:var(--muted)}
.pill{display:inline-block;padding:2px 8px;border-radius:999px;font-size:12px;font-weight:600;
      white-space:nowrap}
.p-dead{background:color-mix(in srgb,var(--dead) 18%,transparent);color:var(--dead)}
.p-warn{background:color-mix(in srgb,var(--warn) 18%,transparent);color:var(--warn)}
.p-soon{background:color-mix(in srgb,var(--soon) 18%,transparent);color:var(--soon)}
.p-ok{background:color-mix(in srgb,var(--ok) 16%,transparent);color:var(--ok)}
.p-none{background:color-mix(in srgb,var(--muted) 14%,transparent);color:var(--muted)}
button{font:inherit;font-size:13px;padding:4px 10px;border-radius:7px;cursor:pointer;
       border:1px solid var(--line);background:transparent;color:var(--fg)}
button:hover{border-color:var(--accent);color:var(--accent)}
form.inline{display:inline;margin:0}
input[type=text],input[type=date]{font:inherit;font-size:13px;padding:4px 8px;border-radius:7px;
       border:1px solid var(--line);background:var(--bg);color:var(--fg);max-width:150px}
.add{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:14px;
     display:flex;flex-wrap:wrap;gap:8px;align-items:center}
.add label{font-size:12px;color:var(--muted);display:flex;flex-direction:column;gap:3px}
.muted{color:var(--muted)}
.log{font-family:ui-monospace,Menlo,monospace;font-size:12px;white-space:pre-wrap}
.v-ok{color:var(--ok)}.v-dup{color:var(--soon)}.v-err{color:var(--dead)}
footer{color:var(--muted);font-size:12px;padding:20px 0;text-align:center}
"""


def _urgency(expiry: str | None) -> tuple[str, str]:
    """Classe CSS et libelle selon l'echeance."""
    if not expiry:
        return "p-none", "sans date"
    d = days_left(expiry)
    if d is None:
        return "p-none", "sans date"
    if d < 0:
        return "p-dead", f"perime ({-d} j)"
    if d == 0:
        return "p-dead", "aujourd'hui"
    if d <= 2:
        return "p-warn", f"J-{d}"
    if d <= 6:
        return "p-soon", f"J-{d}"
    return "p-ok", f"J-{d}"


def _e(value: object) -> str:
    return html.escape("" if value is None else str(value))


def _rows(items, *, consumed: bool = False) -> str:
    out = []
    for r in items:
        cls, label = _urgency(r["expiry"])
        name = r["name"] or "<produit inconnu>"
        qty = f" x{r['qty']}" if r["qty"] > 1 else ""
        brand = f"<div class=code>{_e(r['brand'])}</div>" if r["brand"] else ""
        if consumed:
            action = (f"<form class=inline method=post action=/restore>"
                      f"<input type=hidden name=id value={r['id']}>"
                      f"<button>Remettre</button></form>")
            pill = f"<span class='pill p-none'>sorti</span>"
        else:
            action = (
                f"<form class=inline method=post action=/consume>"
                f"<input type=hidden name=id value={r['id']}>"
                f"<button>Consomme</button></form> "
                f"<form class=inline method=post action=/expiry>"
                f"<input type=hidden name=id value={r['id']}>"
                f"<input type=date name=expiry value='{_e(r['expiry'] or '')}'>"
                f"<button>Date</button></form>"
            )
            pill = f"<span class='pill {cls}'>{_e(label)}</span>"
        naming = ""
        if not r["name"]:
            naming = (f"<form class=inline method=post action=/name>"
                      f"<input type=hidden name=barcode value='{_e(r['barcode'])}'>"
                      f"<input type=text name=name placeholder='nommer...'>"
                      f"<button>OK</button></form>")
        out.append(
            "<tr>"
            f"<td><b>{_e(name)}</b>{_e(qty)}{brand}{naming}</td>"
            f"<td class=code>{_e(r['barcode'])}<br>{_e(bc.describe(r['barcode']))}</td>"
            f"<td>{_e(format_expiry(r['expiry']))}</td>"
            f"<td>{pill}</td>"
            f"<td class=code>{_e(r['location'])}</td>"
            f"<td class=code>{_e(r['device'])}</td>"
            f"<td>{action}</td>"
            "</tr>"
        )
    return "".join(out) or "<tr><td colspan=7 class=muted>Rien ici.</td></tr>"


HEAD_COLS = ("Produit", "Code-barres", "Peremption", "Etat", "Lieu", "Terminal", "")


def _table(items, *, consumed: bool = False) -> str:
    head = "".join(f"<th>{c}</th>" for c in HEAD_COLS)
    return (f"<div class=wrap><table><thead><tr>{head}</tr></thead>"
            f"<tbody>{_rows(items, consumed=consumed)}</tbody></table></div>")


def render_page(store: Store, *, show_consumed: bool = False) -> str:
    s = store.stats()
    urgent = store.expiring(6)
    items = store.inventory(order="expiry")
    tiles = [
        ("presents", s["presents"], "articles"),
        ("perimes", s["perimes"], "perimes"),
        ("urgents", len([r for r in urgent if (days_left(r["expiry"]) or 99) >= 0]), "sous 6 jours"),
        ("sans_date", s["sans_date"], "sans date"),
        ("libelles", s["libelles"], "produits connus"),
    ]
    tiles_html = "".join(
        f"<div class=tile><b>{v}</b><span>{_e(lbl)}</span></div>" for _, v, lbl in tiles
    )
    logs = "".join(
        f"<div class=log><span class='v-{_e(r['verdict'])}'>{_e(r['verdict']).upper():4}</span> "
        f"{_e(r['ts'][11:19])} {_e(r['channel'])} {_e(r['detail'] or r['raw'])}</div>"
        for r in store.recent_log(15)
    ) or "<div class='log muted'>Aucun scan pour l'instant.</div>"

    consumed_block = ""
    if show_consumed:
        consumed_block = ("<h2>Sortis du frigo</h2>"
                          + _table(store.inventory(include_consumed=True,
                                                   order="added"), consumed=True))

    return f"""<!doctype html>
<html lang=fr><head><meta charset=utf-8>
<meta name=viewport content="width=device-width,initial-scale=1">
<title>Frigo</title><style>{CSS}</style>
<meta http-equiv=refresh content=30></head>
<body>
<header><h1>Inventaire du frigo</h1>
<span class=sub>{_e(dt.datetime.now().strftime('%A %d %B %Y, %H:%M'))} &middot;
scans recus du terminal Skorpio</span></header>
<main>
<div class=tiles>{tiles_html}</div>

<h2>Ajouter a la main</h2>
<form class=add method=post action=/add>
  <label>Code-barres<input type=text name=barcode required placeholder=3017620422003></label>
  <label>Peremption<input type=text name=expiry placeholder="25/10/26 ou +7"></label>
  <label>Quantite<input type=text name=qty value=1 style=max-width:60px></label>
  <label>Lieu<input type=text name=location value=frigo style=max-width:90px></label>
  <button>Ajouter</button>
</form>

<h2>A manger en priorite</h2>
{_table(urgent)}

<h2>Tout l'inventaire ({s['presents']})</h2>
{_table(items)}

<h2>Derniers scans</h2>
<div class=wrap style=padding:12px>{logs}</div>
{consumed_block}
<footer>Rafraichissement automatique toutes les 30 s &middot;
<a href="/?consommes=1">voir les articles sortis</a> &middot;
<a href=/api/inventaire>API JSON</a></footer>
</main></body></html>"""


class _Handler(BaseHTTPRequestHandler):
    server_version = "frigo/0.1"
    store: Store
    enricher: Enricher | None
    online: bool

    def log_message(self, fmt: str, *args) -> None:      # silence des acces
        log.debug("%s - %s", self.address_string(), fmt % args)

    # ------------------------------------------------------------------ sorties

    def _send(self, body: str, *, code: int = 200, ctype: str = "text/html; charset=utf-8") -> None:
        payload = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(payload)

    def _redirect(self, to: str = "/") -> None:
        self.send_response(303)
        self.send_header("Location", to)
        self.send_header("Content-Length", "0")
        self.end_headers()

    # -------------------------------------------------------------------- GET

    def do_GET(self) -> None:
        url = urllib.parse.urlparse(self.path)
        qs = urllib.parse.parse_qs(url.query)
        if url.path == "/":
            self._send(render_page(self.store, show_consumed="consommes" in qs))
        elif url.path == "/api/inventaire":
            rows = [dict(r) for r in self.store.inventory()]
            for r in rows:
                r["jours_restants"] = days_left(r["expiry"])
            self._send(json.dumps({"stats": self.store.stats(), "articles": rows},
                                  ensure_ascii=False, indent=2),
                       ctype="application/json; charset=utf-8")
        elif url.path == "/healthz":
            self._send("ok", ctype="text/plain; charset=utf-8")
        else:
            self._send("<h1>404</h1><p><a href=/>retour</a></p>", code=404)

    # ------------------------------------------------------------------- POST

    def do_POST(self) -> None:
        length = int(self.headers.get("Content-Length") or 0)
        if length > 8192:
            self._send("requete trop grande", code=413, ctype="text/plain")
            return
        body = self.rfile.read(length).decode("utf-8", "replace")
        form = {k: v[0] for k, v in urllib.parse.parse_qs(body).items()}
        path = urllib.parse.urlparse(self.path).path
        try:
            self._mutate(path, form)
        except (ValueError, ExpiryError) as exc:
            self._send(f"<h1>Refuse</h1><p>{_e(exc)}</p><p><a href=/>retour</a></p>", code=400)
            return
        self._redirect("/")

    def _mutate(self, path: str, form: dict[str, str]) -> None:
        if path == "/consume":
            self.store.consume(int(form["id"]))
        elif path == "/restore":
            self.store.restore(int(form["id"]))
        elif path == "/expiry":
            value = parse_expiry(form.get("expiry", ""))
            self.store.set_expiry(int(form["id"]), value.isoformat() if value else None)
        elif path == "/name":
            code = bc.normalize(form.get("barcode", ""))
            name = (form.get("name") or "").strip()[:120]
            if code and name:
                self.store.product_upsert(code, name=name, source="manuel")
        elif path == "/add":
            code = bc.normalize(form.get("barcode", ""))
            if not code:
                raise ValueError("code-barres vide")
            expiry = parse_expiry(form.get("expiry", ""))
            qty = int(form.get("qty") or 1)
            self.store.add_item(
                code,
                expiry=expiry.isoformat() if expiry else None,
                qty=max(1, min(qty, 999)),
                location=(form.get("location") or "frigo")[:24],
                device="web",
            )
            if self.online:
                resolve(self.store, code, online=True)
            if self.enricher:
                self.enricher.nudge()
        else:
            raise ValueError(f"action inconnue : {path}")


def make_server(store: Store, *, host: str = "127.0.0.1", port: int = 8077,
                enricher: Enricher | None = None, online: bool = True) -> ThreadingHTTPServer:
    handler = type("Handler", (_Handler,), {
        "store": store, "enricher": enricher, "online": online,
    })
    socketserver.TCPServer.allow_reuse_address = True
    return ThreadingHTTPServer((host, port), handler)


def serve_web(store: Store, **kwargs) -> None:
    srv = make_server(store, **kwargs)
    host, port = srv.server_address[:2]
    log.info("tableau de bord sur http://%s:%s/", host, port)
    srv.serve_forever()
