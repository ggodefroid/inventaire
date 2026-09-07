"""Transports d'ingestion : USB/serie, TCP, fichier de carte memoire.

Les trois transports partagent frigo.ingest.Session, donc le meme protocole et
la meme garantie d'idempotence. Ajouter un transport ne change rien a la base.
"""

from .fileimport import import_path, sniff_format
from .serial_link import SerialLink, find_datalogic_port, list_candidate_ports
from .tcp_link import serve_tcp

__all__ = [
    "SerialLink", "find_datalogic_port", "list_candidate_ports",
    "serve_tcp", "import_path", "sniff_format",
]
