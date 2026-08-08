#!/usr/bin/env bash
# Builds a self-contained cascadia-terminal .deb package.
#
# Usage: packaging/build-deb.sh [version]
# Output: packaging/dist/cascadia-terminal_<version>_amd64.deb
set -euo pipefail

VERSION="${1:-0.1.0}"
# Prefer a user-local SDK when dotnet is not on PATH.
if ! command -v dotnet >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
fi
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$SCRIPT_DIR/dist"
STAGE_DIR="$DIST_DIR/stage"
PUBLISH_DIR="$DIST_DIR/publish"

rm -rf "$STAGE_DIR" "$PUBLISH_DIR"
mkdir -p "$STAGE_DIR" "$PUBLISH_DIR"

echo "==> dotnet publish (self-contained linux-x64)"
dotnet publish "$REPO_DIR/src/CascadiaTerminal/CascadiaTerminal.csproj" \
    -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=false \
    -o "$PUBLISH_DIR"

echo "==> staging package tree"
install -d "$STAGE_DIR/DEBIAN" \
           "$STAGE_DIR/opt/cascadia-terminal" \
           "$STAGE_DIR/usr/bin" \
           "$STAGE_DIR/usr/share/applications" \
           "$STAGE_DIR/usr/share/icons/hicolor/256x256/apps" \
           "$STAGE_DIR/usr/share/icons/hicolor/128x128/apps" \
           "$STAGE_DIR/usr/share/icons/hicolor/48x48/apps"

cp -r "$PUBLISH_DIR"/. "$STAGE_DIR/opt/cascadia-terminal/"

cat > "$STAGE_DIR/usr/bin/cascadia-terminal" <<'LAUNCHER'
#!/bin/sh
exec /opt/cascadia-terminal/CascadiaTerminal "$@"
LAUNCHER
chmod 755 "$STAGE_DIR/usr/bin/cascadia-terminal"

cp "$SCRIPT_DIR/cascadia-terminal.desktop" "$STAGE_DIR/usr/share/applications/"
cp "$SCRIPT_DIR/icon-256.png" "$STAGE_DIR/usr/share/icons/hicolor/256x256/apps/cascadia-terminal.png"
cp "$SCRIPT_DIR/icon-128.png" "$STAGE_DIR/usr/share/icons/hicolor/128x128/apps/cascadia-terminal.png"
cp "$SCRIPT_DIR/icon-48.png"  "$STAGE_DIR/usr/share/icons/hicolor/48x48/apps/cascadia-terminal.png"

INSTALLED_SIZE=$(du -sk "$STAGE_DIR" | cut -f1)
cat > "$STAGE_DIR/DEBIAN/control" <<CONTROL
Package: cascadia-terminal
Version: $VERSION
Section: utils
Priority: optional
Architecture: amd64
Maintainer: Dru Steeby <drusteeby@gmail.com>
Installed-Size: $INSTALLED_SIZE
Depends: libc6, libicu76 | libicu74 | libicu72, libfontconfig1, libx11-6
Description: Windows Terminal experience for Linux
 Cascadia Terminal is an unofficial Linux port of the Windows Terminal
 experience: tabs, split panes, mouse-first interactions, the command
 palette, and Windows Terminal's settings.json format, on the Ghostty
 libghostty-vt emulation core. Self-contained .NET build.
CONTROL

echo "==> building deb"
mkdir -p "$DIST_DIR"
dpkg-deb --build --root-owner-group "$STAGE_DIR" \
    "$DIST_DIR/cascadia-terminal_${VERSION}_amd64.deb"
echo "==> $DIST_DIR/cascadia-terminal_${VERSION}_amd64.deb"
