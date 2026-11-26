#!/bin/sh
set -eu

if [ -n "${PUID:-}" ] && [ "${PUID}" != "0" ]; then
    TARGET_GID="${PGID:-$PUID}"

    if ! getent group "${TARGET_GID}" >/dev/null 2>&1; then
        groupadd -g "${TARGET_GID}" "sonarr_${TARGET_GID}"
    fi

    if ! getent passwd "${PUID}" >/dev/null 2>&1; then
        useradd -u "${PUID}" -g "${TARGET_GID}" -d /config -M -s /usr/sbin/nologin "sonarr_${PUID}"
    fi

    # Only adjust ownership on /config; never touch /tv or /watch
    if [ -d /config ]; then
        chown -R "${PUID}:${TARGET_GID}" /config
    fi

    export HOME=/config
    exec gosu "${PUID}:${TARGET_GID}" dotnet Sonarr.dll -nobrowser -data=/config
fi

exec dotnet Sonarr.dll -nobrowser -data=/config
