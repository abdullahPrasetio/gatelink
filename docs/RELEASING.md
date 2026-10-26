# Releasing GateLink

## Signing key

macOS auto-update verifies a detached RSA-SHA256 signature over the DMG before
mounting it. The public key is pinned in
`src/GateLink.Core/ReleaseSignature.cs`.

The private key was generated into `.secrets/gatelink-release.key` (gitignored).
**Never commit it.** Store it in a password manager and add its base64 form as a
GitHub Actions secret named `GATELINK_RELEASE_KEY`:

```bash
base64 -i .secrets/gatelink-release.key | pbcopy
```

If the secret is absent, CI still publishes the DMG but **does not sign it**;
GateLink will refuse to auto-install that release and will open the download
page instead.

## Release flow

1. Push a tag `vX.Y.Z`.
2. CI builds the macOS `.app` and DMG.
3. The `Sign macOS DMG for auto-update` step produces:
   - `dist/GateLink.dmg.sig` (binary RSA signature)
   - `dist/GateLink.dmg.sha256`
4. The release job uploads the DMG, signature, and checksum.
5. The app downloads the DMG, computes SHA-256, verifies the signature with the
   pinned key, checks the bundle ID and code signature, then swaps the bundle
   with rollback.

Local signing:

```bash
./scripts/sign-release.sh dist/GateLink.dmg .secrets/gatelink-release.key
```

## Key rotation

1. Generate a new RSA-3072 key:
   ```bash
   openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out new.key
   openssl rsa -in new.key -pubout
   ```
2. Replace `PublicKeyPem` in `ReleaseSignature.cs`.
3. Update the `GATELINK_RELEASE_KEY` secret.
4. Ship a release signed with the old key first, or require a manual update.

## Notarization

The current app is **ad-hoc signed**, not Developer ID signed or notarized.
Gatekeeper rejects it on first launch unless the user explicitly allows it. For
public distribution, sign with a Developer ID Application certificate and
notarize with `notarytool`; the update signature is independent of Gatekeeper
and remains required.

Windows releases are not Authenticode signed; SmartScreen may warn. The Windows
update path opens the release page for a manual install.

## Upgrading from a pre-hardening build

Builds before this change have four unsafe behaviours:

- `/etc/sudoers.d/gatelink` grants NOPASSWD to every openconnect path it can
  find (including user-writable repo/app bundles) and to `/bin/kill`;
- the TOTP seed and VPN password live in the login Keychain as generic
  passwords that any process can read through `/usr/bin/security`;
- the full `--authenticate` output (including the WebVPN `COOKIE`) is written to
  `~/gatelink-auth.log`;
- auto-update downloads and replaces the DMG without verifying a signature.

What happens during an upgrade:

1. **First update is trust-on-first-use.** The old app does not check
   `GateLink.dmg.sig`; it will install the new DMG if the release is replaced.
   Prefer a manual install from the signed release and verify the signature
   with the pinned key before opening the new app.
2. **Back up the legacy Keychain items first.** On first launch the new app
   migrates `com.gatelink.vault` to `vault.enc`/`vault.meta.json` and deletes
   the old items. The old app cannot read the new vault afterwards.
3. **Approve the v4 helper install once.** On the first Connect the new app
   runs `install-tunnel-helper.sh`. It overwrites `/etc/sudoers.d/gatelink`
   with the single wrapper rule, removing the old NOPASSWD openconnect/kill
   entries and the old marker, and installs root-owned openconnect,
   `vpnc-script`, `hipreport.sh`, and the wrapper. If the prompt is cancelled,
   the old dangerous rule remains active.
4. **Replace `/Applications/GateLink.app`.** Do not run the old app after the
   upgrade: its `SudoersSetup` can re-add the old NOPASSWD rules and revert the
   helper. Pre-release helper versions 1–3 are overwritten by v4; the old app
   may try to revert those too.
5. **Delete old diagnostic logs.** `~/gatelink-auth.log` may contain a session
   cookie; the old `~/gatelink-tunnel.log` was world-readable. The new code
   writes no auth transcript and creates the tunnel log as `0600`.
6. **Verify the upgrade:**
   ```bash
   sudo -n -ll | grep -A6 gatelink
   sudo -n /usr/local/libexec/gatelink/gatelink-tunnel version   # 4
   security find-generic-password -s com.gatelink.vault -a __index__ -w  # exit 44
   ls ~/Library/Application\ Support/GateLink/vault.*             # enc + meta
   ```
7. **Windows:** the old build stored credentials only in memory, so there is
   nothing to migrate. Install the new build and re-enter the account; DPAPI +
   Windows Hello are now used. The app still requests elevation for the adapter.
8. **HIP:** the new `hipreport.sh` reports actual FileVault/firewall/Time
   Machine state instead of a hardcoded compliant profile. A non-compliant
   machine may now be quarantined by the gateway.
