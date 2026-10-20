#!/usr/bin/env bash
# Registers BrainshEditor as the handler for .bsh files in the current user's
# file managers (Linux, no root needed).
#
# Usage: Tools/linux/install-open-with.sh [/path/to/BrainshEditor.x86_64]
set -euo pipefail

player=${1:-$PWD/Build/Linux/BrainshEditor.x86_64}

if [ ! -x "$player" ]; then
  echo "player not found: $player" >&2
  echo "usage: $0 /path/to/BrainshEditor.x86_64" >&2
  exit 1
fi

player=$(readlink -f "$player")
data=${XDG_DATA_HOME:-$HOME/.local/share}
apps=$data/applications
mime=$data/mime/packages

mkdir -p "$apps" "$mime"

cat > "$mime/brainsh-editor.xml" <<'XML'
<?xml version="1.0" encoding="UTF-8"?>
<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
  <mime-type type="text/x-brainsh">
    <comment>Brainsh script</comment>
    <glob pattern="*.bsh"/>
  </mime-type>
</mime-info>
XML

cat > "$apps/brainsh-editor.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Brainsh Editor
Comment=Brainsh script editor
Exec="$player" %f
Terminal=false
Categories=Development;TextEditor;
MimeType=text/x-brainsh;
StartupNotify=true
DESKTOP

if command -v update-mime-database >/dev/null; then
  update-mime-database "$data/mime"
fi

if command -v xdg-mime >/dev/null; then
  xdg-mime default brainsh-editor.desktop text/x-brainsh
fi

echo "Done: .bsh files now open in $player"
