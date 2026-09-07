"""Stockage SQLite de l'inventaire.

Une seule table porte la verite : `item`. Chaque ligne est une unite physique
presente (ou consommee) dans le frigo, avec sa propre date de peremption --
deux yaourts identiques achetes a deux semaines d'ecart sont deux lignes.

La table `product` n'est qu'un cache de libelles (Open Food Facts ou saisie
manuelle) : on peut la vider sans rien perdre de l'inventaire.

Idempotence : l'index unique (device, seq) fait qu'un tampon de scans renvoye
deux fois par le terminal ne cree jamais de doublon. C'est indispensable avec
une synchronisation sur socle, ou un cable arrache en plein transfert oblige a
tout renvoyer.
"""

from __future__ import annotations

import contextlib
import datetime as dt
import sqlite3
import threading
from pathlib import Path

__all__ = ["Store", "DEFAULT_DB"]

DEFAULT_DB = Path(__file__).resolve().parent.parent / "data" / "inventaire.db"

SCHEMA = """
CREATE TABLE IF NOT EXISTS product (
    barcode     TEXT PRIMARY KEY,
    name        TEXT,
    brand       TEXT,
    pack_size   TEXT,
    categories  TEXT,
    image_url   TEXT,
    source      TEXT,           -- 'openfoodfacts' | 'manuel' | 'inconnu'
    fetched_at  TEXT
);

CREATE TABLE IF NOT EXISTS item (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    device      TEXT NOT NULL DEFAULT 'manuel',
    seq         INTEGER,        -- numero de sequence cote terminal (NULL si saisie PC)
    barcode     TEXT NOT NULL,
    expiry      TEXT,           -- 'YYYY-MM-DD' ou NULL si inconnue
    qty         INTEGER NOT NULL DEFAULT 1,
    location    TEXT NOT NULL DEFAULT 'frigo',
    note        TEXT,
    scanned_at  TEXT,           -- horloge du terminal
    added_at    TEXT NOT NULL,  -- horloge du serveur
    consumed_at TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS item_dedup   ON item(device, seq) WHERE seq IS NOT NULL;
CREATE INDEX        IF NOT EXISTS item_barcode ON item(barcode);
CREATE INDEX        IF NOT EXISTS item_open    ON item(expiry) WHERE consumed_at IS NULL;

CREATE TABLE IF NOT EXISTS scan_log (
    id      INTEGER PRIMARY KEY AUTOINCREMENT,
    ts      TEXT NOT NULL,
    device  TEXT,
    channel TEXT,               -- 'usb' | 'tcp' | 'fichier' | 'web'
    raw     TEXT,
    verdict TEXT,               -- 'ok' | 'dup' | 'err'
    detail  TEXT
);
CREATE INDEX IF NOT EXISTS scan_log_ts ON scan_log(ts);
"""


def _now() -> str:
    return dt.datetime.now().replace(microsecond=0).isoformat()


class Store:
    """Acces SQLite avec une connexion par thread (serie, TCP et HTTP coexistent)."""

    def __init__(self, path: str | Path = DEFAULT_DB) -> None:
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._local = threading.local()
        self._write_lock = threading.Lock()
        # executescript valide implicitement : on l'execute hors transaction.
        with self._write_lock:
            self.cx.executescript(SCHEMA)

    # ---------------------------------------------------------------- plomberie

    @property
    def cx(self) -> sqlite3.Connection:
        cx = getattr(self._local, "cx", None)
        if cx is None:
            cx = sqlite3.connect(self.path, timeout=15.0, isolation_level=None)
            cx.row_factory = sqlite3.Row
            cx.execute("PRAGMA journal_mode=WAL")
            cx.execute("PRAGMA foreign_keys=ON")
            cx.execute("PRAGMA busy_timeout=15000")
            self._local.cx = cx
        return cx

    @contextlib.contextmanager
    def tx(self):
        """Transaction serialisee entre threads : les ecritures restent rares et courtes."""
        with self._write_lock:
            cx = self.cx
            cx.execute("BEGIN IMMEDIATE")
            try:
                yield cx
            except Exception:
                cx.execute("ROLLBACK")
                raise
            else:
                cx.execute("COMMIT")

    def close(self) -> None:
        cx = getattr(self._local, "cx", None)
        if cx is not None:
            cx.close()
            self._local.cx = None

    # ----------------------------------------------------------------- produits

    def product(self, barcode: str) -> sqlite3.Row | None:
        return self.cx.execute(
            "SELECT * FROM product WHERE barcode = ?", (barcode,)
        ).fetchone()

    def product_upsert(
        self,
        barcode: str,
        *,
        name: str | None = None,
        brand: str | None = None,
        pack_size: str | None = None,
        categories: str | None = None,
        image_url: str | None = None,
        source: str = "openfoodfacts",
    ) -> None:
        with self.tx() as cx:
            cx.execute(
                """
                INSERT INTO product (barcode, name, brand, pack_size, categories,
                                     image_url, source, fetched_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                ON CONFLICT(barcode) DO UPDATE SET
                    name       = COALESCE(excluded.name, product.name),
                    brand      = COALESCE(excluded.brand, product.brand),
                    pack_size  = COALESCE(excluded.pack_size, product.pack_size),
                    categories = COALESCE(excluded.categories, product.categories),
                    image_url  = COALESCE(excluded.image_url, product.image_url),
                    source     = excluded.source,
                    fetched_at = excluded.fetched_at
                """,
                (barcode, name, brand, pack_size, categories, image_url, source, _now()),
            )

    def product_label(self, barcode: str) -> str:
        """Libelle court destine a l'ecran 240x320 du terminal."""
        row = self.product(barcode)
        if row and row["name"]:
            label = row["name"]
            if row["pack_size"]:
                label = f"{label} {row['pack_size']}"
            return label[:40]
        return ""

    def products_to_enrich(self, limit: int = 50) -> list[str]:
        """Codes presents dans l'inventaire dont on n'a pas encore de libelle."""
        rows = self.cx.execute(
            """
            SELECT DISTINCT i.barcode
              FROM item i
              LEFT JOIN product p ON p.barcode = i.barcode
             WHERE p.barcode IS NULL
                OR (p.name IS NULL AND COALESCE(p.source,'') <> 'inconnu')
             LIMIT ?
            """,
            (limit,),
        ).fetchall()
        return [r["barcode"] for r in rows]

    # -------------------------------------------------------------------- items

    def add_item(
        self,
        barcode: str,
        *,
        expiry: str | None = None,
        qty: int = 1,
        location: str = "frigo",
        note: str | None = None,
        device: str = "manuel",
        seq: int | None = None,
        scanned_at: str | None = None,
    ) -> tuple[str, int | None]:
        """Insere une unite. Retourne ('ok', id) ou ('dup', id_existant)."""
        if seq is not None:
            existing = self.cx.execute(
                "SELECT id FROM item WHERE device = ? AND seq = ?", (device, seq)
            ).fetchone()
            if existing:
                return "dup", existing["id"]
        try:
            with self.tx() as cx:
                cur = cx.execute(
                    """
                    INSERT INTO item (device, seq, barcode, expiry, qty, location,
                                      note, scanned_at, added_at)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (device, seq, barcode, expiry, qty, location, note,
                     scanned_at, _now()),
                )
                return "ok", cur.lastrowid
        except sqlite3.IntegrityError:
            # Course entre deux transports sur le meme (device, seq).
            row = self.cx.execute(
                "SELECT id FROM item WHERE device = ? AND seq = ?", (device, seq)
            ).fetchone()
            return "dup", row["id"] if row else None

    def last_seq(self, device: str) -> int:
        row = self.cx.execute(
            "SELECT MAX(seq) AS m FROM item WHERE device = ?", (device,)
        ).fetchone()
        return int(row["m"]) if row and row["m"] is not None else 0

    def inventory(
        self,
        *,
        location: str | None = None,
        include_consumed: bool = False,
        order: str = "expiry",
    ) -> list[sqlite3.Row]:
        where = [] if include_consumed else ["i.consumed_at IS NULL"]
        params: list[object] = []
        if location:
            where.append("i.location = ?")
            params.append(location)
        clause = f"WHERE {' AND '.join(where)}" if where else ""
        order_sql = {
            "expiry": "i.expiry IS NULL, i.expiry ASC, i.id ASC",
            "added": "i.added_at DESC, i.id DESC",
            "name": "COALESCE(p.name, i.barcode) COLLATE NOCASE ASC",
        }.get(order, "i.expiry IS NULL, i.expiry ASC")
        return self.cx.execute(
            f"""
            SELECT i.*, p.name, p.brand, p.pack_size, p.image_url, p.source
              FROM item i LEFT JOIN product p ON p.barcode = i.barcode
              {clause}
             ORDER BY {order_sql}
            """,
            params,
        ).fetchall()

    def expiring(self, days: int = 3, *, include_expired: bool = True) -> list[sqlite3.Row]:
        today = dt.date.today()
        limit = (today + dt.timedelta(days=days)).isoformat()
        floor = "'0000-00-00'" if include_expired else f"'{today.isoformat()}'"
        return self.cx.execute(
            f"""
            SELECT i.*, p.name, p.brand, p.pack_size
              FROM item i LEFT JOIN product p ON p.barcode = i.barcode
             WHERE i.consumed_at IS NULL
               AND i.expiry IS NOT NULL
               AND i.expiry <= ?
               AND i.expiry >= {floor}
             ORDER BY i.expiry ASC, i.id ASC
            """,
            (limit,),
        ).fetchall()

    def consume(self, item_id: int) -> bool:
        with self.tx() as cx:
            cur = cx.execute(
                "UPDATE item SET consumed_at = ? WHERE id = ? AND consumed_at IS NULL",
                (_now(), item_id),
            )
            return cur.rowcount > 0

    def consume_barcode(self, barcode: str) -> int | None:
        """Sort du frigo l'unite la plus urgente de ce produit (FEFO)."""
        row = self.cx.execute(
            """
            SELECT id FROM item
             WHERE barcode = ? AND consumed_at IS NULL
             ORDER BY expiry IS NULL, expiry ASC, id ASC LIMIT 1
            """,
            (barcode,),
        ).fetchone()
        if not row:
            return None
        return row["id"] if self.consume(row["id"]) else None

    def restore(self, item_id: int) -> bool:
        with self.tx() as cx:
            cur = cx.execute(
                "UPDATE item SET consumed_at = NULL WHERE id = ?", (item_id,)
            )
            return cur.rowcount > 0

    def set_expiry(self, item_id: int, expiry: str | None) -> bool:
        with self.tx() as cx:
            cur = cx.execute("UPDATE item SET expiry = ? WHERE id = ?", (expiry, item_id))
            return cur.rowcount > 0

    # --------------------------------------------------------------------- logs

    def log(
        self,
        *,
        device: str | None,
        channel: str,
        raw: str,
        verdict: str,
        detail: str = "",
    ) -> None:
        with self.tx() as cx:
            cx.execute(
                "INSERT INTO scan_log (ts, device, channel, raw, verdict, detail)"
                " VALUES (?, ?, ?, ?, ?, ?)",
                (_now(), device, channel, raw[:512], verdict, detail[:512]),
            )

    def recent_log(self, limit: int = 40) -> list[sqlite3.Row]:
        return self.cx.execute(
            "SELECT * FROM scan_log ORDER BY id DESC LIMIT ?", (limit,)
        ).fetchall()

    def stats(self) -> dict[str, int]:
        row = self.cx.execute(
            """
            SELECT
              (SELECT COUNT(*) FROM item WHERE consumed_at IS NULL)            AS presents,
              (SELECT COALESCE(SUM(qty),0) FROM item WHERE consumed_at IS NULL) AS unites,
              (SELECT COUNT(*) FROM item WHERE consumed_at IS NOT NULL)        AS consommes,
              (SELECT COUNT(*) FROM item
                WHERE consumed_at IS NULL AND expiry IS NOT NULL
                  AND expiry < date('now'))                                    AS perimes,
              (SELECT COUNT(*) FROM item WHERE consumed_at IS NULL AND expiry IS NULL)
                                                                               AS sans_date,
              (SELECT COUNT(*) FROM product WHERE name IS NOT NULL)            AS libelles
            """
        ).fetchone()
        return dict(row)
