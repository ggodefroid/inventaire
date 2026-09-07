"""Serveur TCP : utile si le Wi-Fi 802.11b/g du Skorpio est operationnel,
et indispensable pour le simulateur de terminal.

Le meme protocole que la liaison serie circule ici, une Session par connexion.
"""

from __future__ import annotations

import asyncio
import logging

from ..db import Store
from ..ingest import Session
from ..products import Enricher
from ..protocol import LINE_END

__all__ = ["serve_tcp"]

log = logging.getLogger("frigo.tcp")


async def _handle(
    reader: asyncio.StreamReader,
    writer: asyncio.StreamWriter,
    store: Store,
    online: bool,
    enricher: Enricher | None,
    default_location: str,
) -> None:
    peer = writer.get_extra_info("peername")
    log.info("connexion de %s", peer)
    session = Session(store, channel="tcp", online=online, enricher=enricher,
                      default_location=default_location)
    try:
        while True:
            raw = await reader.readline()
            if not raw:
                break
            line = raw.decode("utf-8", "replace")
            for reply in session.handle_line(line):
                writer.write((reply + LINE_END).encode("ascii", "replace"))
            await writer.drain()
    except (ConnectionResetError, asyncio.IncompleteReadError):
        log.info("connexion %s coupee", peer)
    finally:
        log.info("fin de session %s : %s", peer, session.stats)
        writer.close()
        try:
            await writer.wait_closed()
        except (ConnectionResetError, OSError):
            pass


async def serve_tcp(
    store: Store,
    *,
    host: str = "0.0.0.0",
    port: int = 9101,
    online: bool = True,
    enricher: Enricher | None = None,
    default_location: str = "frigo",
) -> None:
    server = await asyncio.start_server(
        lambda r, w: _handle(r, w, store, online, enricher, default_location),
        host=host,
        port=port,
    )
    addrs = ", ".join(str(s.getsockname()) for s in server.sockets)
    log.info("serveur TCP a l'ecoute sur %s", addrs)
    async with server:
        await server.serve_forever()
