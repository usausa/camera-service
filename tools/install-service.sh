#!/usr/bin/env bash
# Places the published service in /opt/camera-service, registers it with systemd and starts it (run as root).
# The service runs as the user camera-service (group video for the cameras) and starts with the system.
# A reinstallation keeps the settings (appsettings*.json) of /opt/camera-service.
set -euo pipefail

readonly name=camera-service
readonly target=/opt/camera-service
readonly unit=/etc/systemd/system/$name.service

if [ "$(id -u)" -ne 0 ]; then
    echo "Run as root (sudo)." >&2
    exit 1
fi

if [ $# -ne 1 ] || [ ! -f "$1/CameraService.Host.dll" ]; then
    echo "Usage: $0 <folder of the published service (CameraService.Host.dll and appsettings.json)>" >&2
    exit 1
fi

source=$(realpath "$1")
tools=$(dirname "$(realpath "$0")")

# libjpeg-turbo of the OS for the JPEG images
if ! grep -q 'libturbojpeg\.so\.0 ' <<< "$(ldconfig -p)"; then
    echo "libturbojpeg.so.0 not found: install the package libturbojpeg (Ubuntu) or libturbojpeg0 (Debian, Raspberry Pi OS)." >&2
    exit 1
fi

# The program of a self-contained publish, otherwise dotnet with the ASP.NET Core runtime
if [ -f "$source/libcoreclr.so" ]; then
    exec_start="$target/CameraService.Host"
else
    version=$(grep -oE '"tfm": *"net[0-9]+' "$source/CameraService.Host.runtimeconfig.json" 2> /dev/null | grep -oE '[0-9]+$' || true)
    if [ -z "$version" ]; then
        echo "Not found: $source/CameraService.Host.runtimeconfig.json" >&2
        exit 1
    fi

    dotnet=$(command -v dotnet || true)
    if [ -z "$dotnet" ] || ! grep -q "^Microsoft\.AspNetCore\.App $version\." <<< "$("$dotnet" --list-runtimes)"; then
        echo "ASP.NET Core $version runtime not found: install it, or publish with --self-contained true." >&2
        exit 1
    fi
    exec_start="$dotnet $target/CameraService.Host.dll"
fi

# The home of the user is the state directory of the service (it keeps the keys of ASP.NET Core)
if ! id "$name" > /dev/null 2>&1; then
    useradd --system --home-dir "/var/lib/$name" --no-create-home --shell /usr/sbin/nologin "$name"
fi
usermod --append --groups video "$name"

# The files of a running service are replaced after it stops
systemctl stop "$name" 2> /dev/null || true
if [ "$source" != "$target" ]; then
    mkdir -p "$target"
    find "$target" -mindepth 1 -maxdepth 1 ! -name 'appsettings*.json' -exec rm -rf {} +
    for entry in "$source"/*; do
        file=$(basename "$entry")
        if [[ "$file" == appsettings*.json && -e "$target/$file" ]]; then
            echo "Kept: $target/$file"
        else
            cp -R "$entry" "$target/"
        fi
    done
fi

chown -R root:root "$target"
chmod -R u=rwX,go=rX "$target"
if [ -f "$target/CameraService.Host" ]; then
    chmod 755 "$target/CameraService.Host"
fi

sed "s|^ExecStart=.*|ExecStart=$exec_start|" "$tools/$name.service" > "$unit"
chmod 644 "$unit"
systemctl daemon-reload
systemctl enable --now "$name"
systemctl --no-pager status "$name" || true
