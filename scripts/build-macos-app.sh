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
dotnet publish "$PROJECT" -c Release -f net9.0 -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -p:Version="$VERSION" >/dev/null

echo "Assembling ${APP} ..."
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Copy published payload into MacOS/
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"

# Privileged helper installer + wrapper. The installer copies the wrapper and
# root-owned assets to /usr/local/libexec/gatelink when the user approves the
# admin prompt; nothing here is executed as root directly.
cp "$ROOT/scripts/install-tunnel-helper.sh" "$APP/Contents/Resources/install-tunnel-helper.sh"
cp "$ROOT/scripts/gatelink-tunnel" "$APP/Contents/Resources/gatelink-tunnel"
chmod +x "$APP/Contents/Resources/install-tunnel-helper.sh" "$APP/Contents/Resources/gatelink-tunnel"

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
  <key>LSMinimumSystemVersion</key>  <string>14.0</string>
  <key>LSUIElement</key>             <true/>
  <key>NSHighResolutionCapable</key> <true/>
</dict>
</plist>
PLIST

chmod +x "$APP/Contents/MacOS/GateLink"
# Ensure bundled helpers stay executable after copy.
[ -f "$APP/Contents/MacOS/Assets/openconnect/openconnect" ] && chmod +x "$APP/Contents/MacOS/Assets/openconnect/openconnect"
[ -f "$APP/Contents/MacOS/Assets/openconnect/vpnc-script" ] && chmod +x "$APP/Contents/MacOS/Assets/openconnect/vpnc-script"
[ -f "$APP/Contents/MacOS/Assets/hipreport.sh" ] && chmod +x "$APP/Contents/MacOS/Assets/hipreport.sh"

# Compile the LocalAuthentication helper. The reason is passed as argv; no
# untrusted text is interpolated into source.
if command -v swiftc >/dev/null 2>&1; then
  swiftc -O -target "$(uname -m)-apple-macos14.0" \
    "$ROOT/src/GateLink.Desktop/Platform/GateLinkAuth.swift" \
    -o "$APP/Contents/MacOS/Assets/GateLinkAuth" 2>/dev/null && \
    chmod +x "$APP/Contents/MacOS/Assets/GateLinkAuth" || \
    echo "  (GateLinkAuth compile failed; Touch ID falls back to the Keychain prompt)"
else
  echo "  (swiftc not found; Touch ID falls back to the Keychain prompt)"
fi

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
