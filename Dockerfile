# syntax=docker/dockerfile:1.7
#
# Image unique, deux roles : serveur.py pour le terminal, vitrine.py pour le
# site public. Ce qui les distingue -- port, droit d'ecrire, sonde -- est
# decide au demarrage par docker-compose.yml.

ARG PYTHON=3.14

# --------------------------------------------------------------- assemblage

FROM python:${PYTHON}-slim AS assemblage

RUN python -m venv /opt/venv
ENV PATH=/opt/venv/bin:$PATH

COPY requirements.txt /tmp/requirements.txt

# Les roues precompilees couvrent x86-64 et arm64. Ailleurs -- un Raspberry Pi
# 32 bits -- Pillow doit etre compile : la chaine n'est installee que dans ce
# cas, et reste dans cet etage, que l'image finale ne recopie pas.
RUN set -eu; \
    if ! pip install --no-cache-dir --only-binary=:all: \
            -r /tmp/requirements.txt 'gunicorn>=26,<27'; then \
        echo "pas de roue pour cette architecture, compilation de Pillow"; \
        apt-get update; \
        apt-get install -y --no-install-recommends \
            gcc libc6-dev libjpeg-dev zlib1g-dev; \
        pip install --no-cache-dir -r /tmp/requirements.txt 'gunicorn>=26,<27'; \
    fi; \
    find /opt/venv -name '__pycache__' -type d -prune -exec rm -rf {} +

# ---------------------------------------------------------------- execution

FROM python:${PYTHON}-slim AS execution

ARG UID=1000
ARG GID=1000

# Sans tzdata, TZ ne fait rien et la libc retombe sur UTC. Or chaque reponse
# porte `aujourdhui=` et `heure=`, que le terminal affiche tel quel faute
# d'horloge fiable : une heure decalee est une peremption fausse.
RUN apt-get update \
 && apt-get install -y --no-install-recommends tzdata \
 && rm -rf /var/lib/apt/lists/*

# L'UID peut deja exister dans l'image de base, et compose peut de toute facon
# en imposer un autre au demarrage : l'echec n'est pas fatal.
RUN groupadd --gid "$GID" frigo 2>/dev/null || true; \
    useradd --uid "$UID" --gid "$GID" --no-create-home \
            --home-dir /app --shell /usr/sbin/nologin frigo 2>/dev/null || true

COPY --from=assemblage /opt/venv /opt/venv

WORKDIR /app
COPY backend/ /app/backend/
COPY docker/ /app/docker/
RUN chmod +x /app/docker/entree.sh

# Points de montage. Des qu'un montage lie les recouvre, ce sont les droits de
# l'hote qui comptent.
RUN mkdir -p /donnees/cache /dist && chown -R "$UID:$GID" /donnees

ENV PATH=/opt/venv/bin:$PATH \
    PYTHONPATH=/app/backend \
    PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    TZ=Europe/Paris \
    FRIGO_ADRESSE=0.0.0.0 \
    FRIGO_DB=/donnees/inventaire.db \
    FRIGO_CACHE=/donnees/cache \
    FRIGO_DIST=/dist

USER $UID:$GID
EXPOSE 8080 8081

ENTRYPOINT ["/app/docker/entree.sh"]
CMD ["serveur"]
