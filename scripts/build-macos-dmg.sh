#!/usr/bin/env bash
#
# build-macos-dmg.sh — package GateLink.app into a drag-to-install DMG.
#
# Produces dist/GateLink.dmg containing GateLink.app and an Applications
# shortcut, so the user just drags the app into Applications.
#
# USAGE
#   ./scripts/build-macos-dmg.sh          # builds the .app first, then the dmg
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DIST="$ROOT/dist"
APP="$DIST/GateLink.app"
DMG="$DIST/GateLink.dmg"
STAGE="$DIST/dmg-stage"
VOLNAME="GateLink"

# 1. Make sure the .app exists (build it if needed).
if [ ! -d "$APP" ]; then
  echo "GateLink.app not found; building it first..."
  "$ROOT/scripts/build-macos-app.sh"
fi

# 2. Stage a folder with the app + an Applications symlink.
echo "Staging DMG contents..."
rm -rf "$STAGE" "$DMG"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"

# 3. Build a compressed DMG from the staging folder.
echo "Creating $DMG ..."
hdiutil create \
  -volname "$VOLNAME" \
  -srcfolder "$STAGE" \
  -ov \
  -format UDZO \
  "$DMG" >/dev/null

rm -rf "$STAGE"

echo
echo "Built: $DMG"
echo "Install: open the DMG, then drag GateLink into Applications."
