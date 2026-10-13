#!/usr/bin/env sh
set -eu
if [ "${1:-}" = "--help" ] || [ "${1:-}" = "-h" ]; then
  echo "Usage: $0 [--autostart]"
  exit 0
fi
if [ "$#" -gt 1 ] || { [ "$#" -eq 1 ] && [ "$1" != "--autostart" ]; }; then
  echo "Usage: $0 [--autostart]"
  exit 1
fi
install_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
if [ ! -x "$install_dir/Frpm.Desktop" ]; then
  echo "Run this script from an extracted FRPM desktop package."
  exit 1
fi
case "$install_dir" in
  *'"'*|*'\'*|*'%'*) echo "Choose an installation path without quotes, backslashes or percent signs."; exit 1 ;;
esac
desktop_dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$desktop_dir"
cat > "$desktop_dir/frpm.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=FRPM
Comment=FRP tunnel manager
Comment[zh_CN]=内网穿透管理
Exec="$install_dir/Frpm.Desktop"
Path=$install_dir
Terminal=false
Categories=Network;Utility;
EOF
if [ "${1:-}" = "--autostart" ]; then
  autostart_dir="${XDG_CONFIG_HOME:-$HOME/.config}/autostart"
  mkdir -p "$autostart_dir"
  sed 's|^Exec=.*|& --background|' "$desktop_dir/frpm.desktop" > "$autostart_dir/frpm.desktop"
fi
echo "FRPM has been added to your application menu. Keep this directory in place: $install_dir"
