#!/usr/bin/env sh
set -eu

if [ "${1:-}" = "--help" ] || [ "${1:-}" = "-h" ]; then
  echo "Usage: $0"
  echo "Install the native FRPM runtime as a systemd user service (no Web port)."
  exit 0
fi
if [ "$#" -ne 0 ]; then
  echo "The native desktop runtime has no management port. --port/-p is no longer needed."
  exit 1
fi
if [ "$(id -u)" -eq 0 ]; then
  echo "Run this script as your desktop user, without sudo."
  exit 1
fi

install_dir="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
if [ ! -x "$install_dir/Frpm.Desktop" ]; then
  echo "Run this script from an extracted FRPM desktop package."
  exit 1
fi
case "$install_dir" in
  *'"'*|*'\'*) echo "Choose an installation path without quotes or backslashes."; exit 1 ;;
esac
config_root="${XDG_CONFIG_HOME:-$HOME/.config}"
unit_directory="$config_root/systemd/user"
mkdir -p "$unit_directory"
escaped_dir="$(printf '%s' "$install_dir" | sed 's/%/%%/g; s/[&|]/\\&/g')"
sed "s|__FRPM_INSTALL_DIR__|$escaped_dir|g" "$install_dir/systemd/frpm.service" > "$unit_directory/frpm.service"
systemctl --user daemon-reload
systemctl --user enable --now frpm.service
echo "FRPM is running without a Web server. Check: systemctl --user status frpm"
echo "Stop this service before opening the desktop UI: systemctl --user stop frpm"
