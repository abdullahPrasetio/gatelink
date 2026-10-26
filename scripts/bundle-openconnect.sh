#!/usr/bin/env bash
#
# bundle-openconnect.sh — collect openconnect + its dylibs into the app bundle
# so GateLink ships a working VPN engine and users don't need `brew install`.
#
# It copies the openconnect binary, every non-system dylib it needs
# (recursively), and vpnc-script into a staging folder, then rewrites all the
# Mach-O load paths to be relative (@loader_path), making the set relocatable.
#
# Output: src/GateLink.Desktop/Assets/openconnect/  (bundled into the .app later)
#
# Run this on a Mac that already has a working openconnect (e.g. via Homebrew).
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/src/GateLink.Desktop/Assets/openconnect"
OC="$(command -v openconnect || echo /opt/homebrew/bin/openconnect)"

if [ ! -x "$OC" ]; then
  echo "ERROR: openconnect not found. Install it first (brew install openconnect)." >&2
  exit 1
fi

echo "Bundling from: $OC"
rm -rf "$OUT"
mkdir -p "$OUT"

# 1. Gather the binary + all non-system dylibs it needs (recursively).
python3 - "$OC" "$OUT" <<'PY'
import subprocess, sys, os, shutil
oc, out = sys.argv[1], sys.argv[2]

def deps(f):
    try:
        lines = subprocess.check_output(["otool","-L",f], text=True).splitlines()[1:]
    except Exception:
        return []
    r = []
    for ln in lines:
        p = ln.strip().split(" ")[0]
        if p.startswith("/opt/homebrew") or p.startswith("/usr/local"):
            r.append(p)
    return r

# Map each referenced path to the name it is referenced BY (not realpath), so
# the copied file name matches what the Mach-O loader looks for.
seen=set(); queue=[oc]; refs={}   # refs[basename-as-referenced] = real file to copy
while queue:
    f = queue.pop()
    key = os.path.basename(f)
    if key in seen: continue
    seen.add(key)
    real = os.path.realpath(f)
    if f != oc:
        refs[key] = real
    for d in deps(real):
        queue.append(d)

# Copy the binary as "openconnect"
shutil.copy2(os.path.realpath(oc), os.path.join(out, "openconnect"))
os.chmod(os.path.join(out, "openconnect"), 0o755)

# Copy each dylib under the exact name it is referenced by.
for name, real in refs.items():
    shutil.copy2(real, os.path.join(out, name))

print("copied", len(refs), "dylibs +", "openconnect")
PY

# 2. Rewrite load paths to @loader_path so the set is relocatable.
echo "Rewriting dylib paths..."
cd "$OUT"
for f in openconnect *.dylib; do
  [ -f "$f" ] || continue
  # For each non-system dependency, repoint it to @loader_path/<basename>.
  otool -L "$f" | tail -n +2 | awk '{print $1}' | while read -r dep; do
    case "$dep" in
      /opt/homebrew/*|/usr/local/*)
        base="$(basename "$dep")"
        install_name_tool -change "$dep" "@loader_path/$base" "$f" 2>/dev/null || true
        ;;
    esac
  done
  # Fix the library's own id too.
  if [[ "$f" == *.dylib ]]; then
    install_name_tool -id "@loader_path/$f" "$f" 2>/dev/null || true
  fi
  # Re-sign ad-hoc after modification (required on Apple Silicon).
  codesign --force --sign - "$f" 2>/dev/null || true
done

# 3. Bundle vpnc-script (needed to configure routes on connect).
VPNC="$(find /opt/homebrew /usr/local -name vpnc-script 2>/dev/null | head -1)"
if [ -n "$VPNC" ]; then
  cp "$VPNC" "$OUT/vpnc-script"
  chmod +x "$OUT/vpnc-script"
  echo "bundled vpnc-script"
fi

echo
echo "Done. Bundled openconnect into: $OUT"
ls -la "$OUT"
