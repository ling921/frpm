#!/usr/bin/env sh
set -eu

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

sed "s|__FRPM_INSTALL_DIR__|$install_dir|g" "$unit_source" > "$unit_target"
systemctl daemon-reload
systemctl enable --now frpm.service
echo "FRPM is running as a systemd service. Open http://127.0.0.1:8180 on this machine."
