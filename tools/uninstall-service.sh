#!/usr/bin/env bash
# Stops and removes the camera service and /opt/camera-service with its settings (run as root).
# The images (/var/lib/camera-service), the log files (/var/log/camera-service) and the user camera-service are kept.
set -euo pipefail

readonly name=camera-service

if [ "$(id -u)" -ne 0 ]; then
    echo "Run as root (sudo)." >&2
    exit 1
fi

systemctl disable --now "$name" 2> /dev/null || true
rm -f "/etc/systemd/system/$name.service"
systemctl daemon-reload
rm -rf /opt/camera-service
