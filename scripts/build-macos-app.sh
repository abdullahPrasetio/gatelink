#!/usr/bin/env bash
#
# build-macos-app.sh — package GateLink as a double-clickable macOS .app bundle.
#
# Produces dist/GateLink.app: a self-contained menu bar application (no dock
# icon, thanks to LSUIElement) that bundles the .NET runtime, so it runs without
# `dotnet` installed.
#
# USAGE
#   ./scripts/build-macos-app.sh
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/src/GateLink.Desktop/GateLink.Desktop.csproj"
ASSETS="$ROOT/src/GateLink.Desktop/Assets"
DIST="$ROOT/dist"
APP="$DIST/GateLink.app"
RID="osx-$(uname -m | sed 's/x86_64/x64/;s/arm64/arm64/')"

# Version comes from CI (GATELINK_VERSION, set from the git tag); default for
# local dev builds is 0.0.0 so they never look "newer" than a real release.
VERSION="${GATELINK_VERSION:-0.0.0}"

echo "Building self-contained publish for ${RID} (version ${VERSION}) ..."
PUBLISH="$ROOT/src/GateLink.Desktop/bin/Release/net9.0/$RID/publish"
dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -p:Version="$VERSION" >/dev/null

echo "Assembling ${APP} ..."
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Copy published payload into MacOS/
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"

# Icon
[ -f "$ASSETS/GateLink.icns" ] && cp "$ASSETS/GateLink.icns" "$APP/Contents/Resources/GateLink.icns"

# Info.plist — LSUIElement=true makes it a menu bar app with no dock icon.
# Unquoted heredoc so ${VERSION} is interpolated.
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>            <string>GateLink</string>
  <key>CFBundleDisplayName</key>     <string>GateLink</string>
  <key>CFBundleIdentifier</key>      <string>com.gatelink.app</string>
  <key>CFBundleVersion</key>         <string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundlePackageType</key>     <string>APPL</string>
  <key>CFBundleExecutable</key>      <string>GateLink</string>
  <key>CFBundleIconFile</key>        <string>GateLink</string>
  <key>LSMinimumSystemVersion</key>  <string>12.0</string>
  <key>LSUIElement</key>             <true/>
  <key>NSHighResolutionCapable</key> <true/>
</dict>
</plist>
PLIST

chmod +x "$APP/Contents/MacOS/GateLink"

# Ad-hoc codesign so Gatekeeper/hardened features behave on the local machine.
if command -v codesign >/dev/null; then
  echo "Ad-hoc signing ..."
  codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || \
    echo "  (ad-hoc sign skipped/failed; app still runs locally)"
fi

echo
echo "Built: $APP"
echo "Run it: open \"$APP\"   (or double-click in Finder)"
echo "Install: drag GateLink.app into /Applications"
