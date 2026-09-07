"""Liaison serie : socle RS-232 du Skorpio, ou son port USB via le module ipaq.

Contrainte propre au socle : le port apparait et disparait au rythme des poses
et des retraits du terminal. La boucle principale traite donc l'absence de port
comme un etat normal, pas comme une erreur, et se rattache toute seule.
"""

from __future__ import annotations

import logging
import time
from pathlib import Path

try:
    import serial
    from serial.tools import list_ports
except ImportError as exc:  # pragma: no cover
    raise SystemExit(
        "pyserial est requis pour la liaison serie : pip install pyserial"
    ) from exc

from ..db import Store
from ..ingest import Session
from ..products import Enricher
from ..protocol import LINE_END

__all__ = ["SerialLink", "find_datalogic_port", "list_candidate_ports"]

log = logging.getLogger("frigo.serie")

DATALOGIC_VID = 0x080C          # Datalogic S.p.A.
USB_SYNC_PID = 0x0200           # "Datalogic USB Sync"


def list_candidate_ports(*, include_phantom: bool = False) -> list[dict[str, str]]:
    """Ports serie visibles, avec le VID/PID quand c'est de l'USB.

    Les cartes meres exposent une trentaine de /dev/ttyS* qui ne correspondent a
    aucun materiel : on les masque par defaut pour que la liste reste lisible.
    """
    out = []
    for p in list_ports.comports():
        phantom = p.vid is None and (p.description or "n/a") == "n/a"
        if phantom and not include_phantom:
            continue
        out.append({
            "device": p.device,
            "description": p.description or "",
            "vid": f"0x{p.vid:04X}" if p.vid else "",
            "pid": f"0x{p.pid:04X}" if p.pid else "",
            "serial": p.serial_number or "",
            "datalogic": "oui" if p.vid == DATALOGIC_VID else "",
        })
    return out


def find_datalogic_port() -> str | None:
    """Repere le port du terminal : d'abord par VID Datalogic, sinon heuristique."""
    ports = list_ports.comports()
    for p in ports:
        if p.vid == DATALOGIC_VID:
            log.info("terminal Datalogic detecte sur %s (PID 0x%04X)", p.device, p.pid or 0)
            return p.device
    # Le module ipaq n'expose pas toujours le VID : on retombe sur le ttyUSB seul.
    usb = [p.device for p in ports if "USB" in p.device.upper()]
    if len(usb) == 1:
        log.info("un seul port USB-serie present, on prend %s", usb[0])
        return usb[0]
    return None


class SerialLink:
    """Boucle de service sur un port serie, resistante aux (de)connexions."""

    def __init__(
        self,
        store: Store,
        port: str | None = None,
        *,
        baudrate: int = 115200,
        online: bool = True,
        enricher: Enricher | None = None,
        default_location: str = "frigo",
        read_timeout: float = 1.0,
        retry_delay: float = 2.0,
    ) -> None:
        self.store = store
        self.port = port
        self.baudrate = baudrate
        self.online = online
        self.enricher = enricher
        self.default_location = default_location
        self.read_timeout = read_timeout
        self.retry_delay = retry_delay
        self._stop = False

    def stop(self) -> None:
        self._stop = True

    # ------------------------------------------------------------------ boucle

    def run_forever(self) -> None:
        announced_missing = False
        while not self._stop:
            port = self.port or find_datalogic_port()
            if not port or not self._port_exists(port):
                if not announced_missing:
                    log.info("en attente du terminal (aucun port serie disponible)...")
                    announced_missing = True
                time.sleep(self.retry_delay)
                continue
            announced_missing = False
            try:
                self._serve_once(port)
            except serial.SerialException as exc:
                log.info("liaison %s interrompue : %s", port, exc)
            except OSError as exc:
                log.info("port %s indisponible : %s", port, exc)
            time.sleep(self.retry_delay)

    @staticmethod
    def _port_exists(port: str) -> bool:
        # Un /dev/ttyUSB* disparait des que le terminal quitte son socle.
        return not port.startswith("/dev/") or Path(port).exists()

    def _serve_once(self, port: str) -> None:
        log.info("ouverture de %s a %d bauds", port, self.baudrate)
        with serial.Serial(
            port,
            baudrate=self.baudrate,
            bytesize=serial.EIGHTBITS,
            parity=serial.PARITY_NONE,
            stopbits=serial.STOPBITS_ONE,
            timeout=self.read_timeout,
            write_timeout=5.0,
            rtscts=False,
            dsrdtr=False,
        ) as link:
            link.reset_input_buffer()
            session = Session(
                self.store,
                channel="usb",
                online=self.online,
                enricher=self.enricher,
                default_location=self.default_location,
            )
            log.info("terminal en ligne sur %s, en attente de scans", port)
            self._pump(link, session)
            log.info("session terminee sur %s : %s", port, session.stats)

    def _pump(self, link: "serial.Serial", session: Session) -> None:
        """Lit ligne par ligne et repond. Sortie quand le port meurt."""
        buffer = bytearray()
        while not self._stop:
            chunk = link.read(256)
            if not chunk:
                if not self._port_exists(link.port):
                    return
                continue
            buffer.extend(chunk)
            if len(buffer) > 64 * 1024:      # garde-fou anti-flux binaire
                log.warning("tampon serie sature, purge (flux non protocolaire ?)")
                del buffer[:-1024]
            while True:
                idx = buffer.find(b"\n")
                if idx < 0:
                    break
                line = bytes(buffer[:idx]).decode("utf-8", "replace")
                del buffer[: idx + 1]
                for reply in session.handle_line(line):
                    link.write((reply + LINE_END).encode("ascii", "replace"))
                link.flush()
