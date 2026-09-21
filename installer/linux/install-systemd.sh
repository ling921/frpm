#!/usr/bin/env sh
set -eu

port=""

usage() {
  echo "Usage: $0 [--port PORT|-p PORT]"
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    -p|--port)
      if [ "$#" -lt 2 ]; then
        usage
        exit 1
      fi
      port="$2"
      shift 2
      ;;
    --port=*)
      port="${1#*=}"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      usage
      exit 1
      ;;
  esac
done

if [ -n "$port" ]; then
  case "$port" in
    *[!0-9]*)
      echo "Port must be an integer from 1 to 65535."
      exit 1
      ;;
  esac
  if [ "$port" -lt 1 ] || [ "$port" -gt 65535 ]; then
    echo "Port must be an integer from 1 to 65535."
    exit 1
  fi
fi

if [ "$(id -u)" -ne 0 ]; then
  echo "Run this script with sudo."
  exit 1
fi

install_dir="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
unit_source="$install_dir/systemd/frpm.service"
unit_target="/etc/systemd/system/frpm.service"

if [ ! -x "$install_dir/Frpm" ] || [ ! -f "$unit_source" ]; then
  echo "Run this script from an extracted FRPM release package."
  exit 1
fi

if [ -n "$port" ]; then
  printf '{\n  "Urls": "http://+:%s"\n}\n' "$port" > "$install_dir/appsettings.Production.json"
fi

sed "s|__FRPM_INSTALL_DIR__|$install_dir|g" "$unit_source" > "$unit_target"
systemctl daemon-reload
systemctl enable frpm.service
systemctl restart frpm.service

if [ -z "$port" ]; then
  port="8180"
fi
echo "FRPM is running as a systemd service. Open http://127.0.0.1:$port on this machine."
