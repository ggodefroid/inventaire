"""Recuperation des libelles produits depuis Open Food Facts.

Le Skorpio ne connait que des chiffres : c'est le PC qui transforme
3017620422003 en "Nutella pate a tartiner 400 g". Trois regles :

1. Le cache SQLite est autoritaire. Une fois un code resolu, plus de reseau.
2. Un code introuvable est marque 'inconnu' pour ne pas etre re-interroge en
   boucle a chaque synchronisation.
3. L'ingestion ne depend jamais du reseau. Un scan arrive et est enregistre
   meme hors ligne ; l'enrichissement se fait en tache de fond.
"""

from __future__ import annotations

import json
import logging
import threading
import time
import urllib.error
import urllib.request

from .db import Store

__all__ = ["OffError", "fetch", "resolve", "Enricher", "USER_AGENT"]

log = logging.getLogger("frigo.products")

OFF_ENDPOINT = "https://world.openfoodfacts.org/api/v2/product/{code}.json"
OFF_FIELDS = ",".join([
    "code", "product_name", "product_name_fr", "generic_name_fr", "abbreviated_product_name",
    "brands", "quantity", "categories", "image_front_small_url",
])
# Open Food Facts demande un User-Agent identifiable pour ne pas etre limite.
USER_AGENT = "inventaire-frigo/0.1 (Datalogic Skorpio, usage domestique)"


class OffError(RuntimeError):
    """Probleme reseau ou reponse illisible. A distinguer d'un produit absent."""


def _pick_name(p: dict) -> str | None:
    for key in ("product_name_fr", "product_name", "abbreviated_product_name",
                "generic_name_fr"):
        value = (p.get(key) or "").strip()
        if value:
            return value
    return None


def fetch(barcode: str, *, timeout: float = 8.0) -> dict | None:
    """Interroge Open Food Facts. None = produit absent du catalogue."""
    url = OFF_ENDPOINT.format(code=barcode) + "?fields=" + OFF_FIELDS
    req = urllib.request.Request(url, headers={
        "User-Agent": USER_AGENT,
        "Accept": "application/json",
    })
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            payload = json.loads(resp.read().decode("utf-8", "replace"))
    except urllib.error.HTTPError as exc:
        if exc.code == 404:
            return None
        raise OffError(f"HTTP {exc.code} sur {barcode}") from exc
    except (urllib.error.URLError, TimeoutError, OSError) as exc:
        raise OffError(f"reseau indisponible : {exc}") from exc
    except json.JSONDecodeError as exc:
        raise OffError(f"reponse illisible pour {barcode}") from exc

    if not payload.get("product") or payload.get("status") == 0:
        return None
    p = payload["product"]
    name = _pick_name(p)
    if not name:
        return None
    return {
        "name": name[:120],
        "brand": (p.get("brands") or "").split(",")[0].strip()[:60] or None,
        "pack_size": (p.get("quantity") or "").strip()[:30] or None,
        "categories": (p.get("categories") or "").strip()[:200] or None,
        "image_url": p.get("image_front_small_url") or None,
    }


def resolve(
    store: Store,
    barcode: str,
    *,
    online: bool = True,
    refresh: bool = False,
    timeout: float = 8.0,
) -> str:
    """Retourne un libelle, en interrogeant le reseau si le cache est vide.

    Ne leve jamais : hors ligne, on rend une chaine vide et l'enrichisseur
    reessaiera plus tard.
    """
    if not barcode:
        return ""
    if not refresh:
        row = store.product(barcode)
        if row is not None and (row["name"] or row["source"] == "inconnu"):
            return store.product_label(barcode)
    if not online:
        return store.product_label(barcode)

    try:
        data = fetch(barcode, timeout=timeout)
    except OffError as exc:
        log.info("enrichissement differe pour %s : %s", barcode, exc)
        return store.product_label(barcode)

    if data is None:
        store.product_upsert(barcode, source="inconnu")
        return ""
    store.product_upsert(barcode, source="openfoodfacts", **data)
    return store.product_label(barcode)


class Enricher(threading.Thread):
    """Tache de fond qui comble les libelles manquants, sans bloquer l'ingestion."""

    def __init__(
        self,
        store: Store,
        *,
        interval: float = 20.0,
        pause: float = 0.4,
        batch: int = 25,
        online: bool = True,
    ) -> None:
        super().__init__(name="enricher", daemon=True)
        self.store = store
        self.interval = interval
        self.pause = pause          # courtoisie envers l'API publique
        self.batch = batch
        self.online = online
        self._wake = threading.Event()
        self._stop = threading.Event()

    def nudge(self) -> None:
        """Reveille l'enrichisseur : appele apres chaque lot de scans recu."""
        self._wake.set()

    def stop(self) -> None:
        self._stop.set()
        self._wake.set()

    def run(self) -> None:
        while not self._stop.is_set():
            if self.online:
                try:
                    self._pass()
                except Exception:  # une tache de fond ne doit jamais tuer le serveur
                    log.exception("echec d'un cycle d'enrichissement")
            self._wake.wait(self.interval)
            self._wake.clear()

    def _pass(self) -> int:
        codes = self.store.products_to_enrich(self.batch)
        done = 0
        for code in codes:
            if self._stop.is_set():
                break
            label = resolve(self.store, code, online=True)
            if label:
                log.info("libelle trouve : %s -> %s", code, label)
                done += 1
            time.sleep(self.pause)
        return done
