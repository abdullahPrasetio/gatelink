#!/usr/bin/env bash
#
# install-sudoers.sh — allow GateLink to run openconnect as root without a
# password prompt (macOS / Linux).
#
# WHAT THIS DOES
#   Installs /etc/sudoers.d/gatelink granting the CURRENT user NOPASSWD rights
#   to run the detected openconnect binary and to kill the tunnel process.
#
# SECURITY WARNING
#   NOPASSWD for openconnect effectively allows running code as root for anyone
#   who can act as your user, because openconnect can execute scripts. Install
#   this only on a machine you trust and understand the trade-off. To undo, run
#   this script with the `uninstall` argument.
#
# USAGE
#   ./install-sudoers.sh            # install
#   ./install-sudoers.sh uninstall  # remove
#
set -euo pipefail

SUDOERS_FILE="/etc/sudoers.d/gatelink"
TARGET_USER="$(id -un)"

uninstall() {
  echo "Removing ${SUDOERS_FILE} (requires sudo)…"
  sudo rm -f "${SUDOERS_FILE}"
  echo "Done. GateLink can no longer run openconnect without a password."
  exit 0
}

[[ "${1:-}" == "uninstall" ]] && uninstall

# --- locate openconnect -------------------------------------------------------
OPENCONNECT="$(command -v openconnect || true)"
for cand in /opt/homebrew/bin/openconnect /usr/local/bin/openconnect /usr/bin/openconnect; do
  [[ -z "${OPENCONNECT}" && -x "${cand}" ]] && OPENCONNECT="${cand}"
done

if [[ -z "${OPENCONNECT}" ]]; then
  echo "ERROR: openconnect not found. Install it first (brew install openconnect)." >&2
  exit 1
fi

# locate kill (used to stop the root tunnel by pid)
KILL_BIN="/bin/kill"
[[ -x "${KILL_BIN}" ]] || KILL_BIN="$(command -v kill)"

echo "User:        ${TARGET_USER}"
echo "openconnect: ${OPENCONNECT}"
echo "kill:        ${KILL_BIN}"
echo "Target file: ${SUDOERS_FILE}"
echo

# --- build the rule in a temp file, validate, then install --------------------
TMP="$(mktemp)"
trap 'rm -f "${TMP}"' EXIT

cat > "${TMP}" <<EOF
# Installed by GateLink (install-sudoers.sh). Remove with: install-sudoers.sh uninstall
${TARGET_USER} ALL=(root) NOPASSWD: ${OPENCONNECT}
${TARGET_USER} ALL=(root) NOPASSWD: ${KILL_BIN}
EOF

echo "The following rule will be installed:"
echo "--------------------------------------"
cat "${TMP}"
echo "--------------------------------------"
echo

# Validate syntax against the live sudoers config BEFORE installing, so a typo
# can never lock you out of sudo.
echo "Validating with visudo…"
sudo visudo -c -f "${TMP}"

echo "Installing (requires sudo)…"
sudo install -m 0440 -o root -g wheel "${TMP}" "${SUDOERS_FILE}" 2>/dev/null \
  || sudo install -m 0440 -o root "${TMP}" "${SUDOERS_FILE}"

echo
echo "Installed. GateLink can now bring up the tunnel without a password prompt."
echo "To undo: ./install-sudoers.sh uninstall"
