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

## Architecture-specific assets

macOS releases ship two apps, both requiring macOS 14:

| Asset | Architecture |
|-------|--------------|
| `GateLink-arm64.dmg` | Apple Silicon |
| `GateLink-x64.dmg` | Intel |

`GateLink.dmg` is a byte-identical copy of the arm64 DMG kept so apps released
before dual-arch support can still auto-update. `UpdateChecker` picks the DMG
matching `RuntimeInformation.ProcessArchitecture`; an Intel app never falls
back to the arm64 legacy file, and a missing signature makes the updater open
the release page instead of installing.

## Release flow

1. Push a tag `vX.Y.Z`.
2. CI builds both macOS apps in a matrix:
   - `macos-15` → arm64
   - `macos-15-intel` → x64
3. Each job builds the OpenConnect engine from pinned sources with
   `-mmacosx-version-min=14.0` (see below), bundles it into the `.app`, and
   builds one DMG.
4. On tags, the `Sign macOS DMG for auto-update` step produces:
   - `dist/GateLink-arm64.dmg.sig` and `.sha256` (also copied to the legacy
     `GateLink.dmg.sig` / `.sha256`)
   - `dist/GateLink-x64.dmg.sig` and `.sha256`
5. The release job uploads both DMGs, both signatures, both checksums, the
   legacy arm64 copy, and the two zips.
6. The app downloads the DMG for its own CPU, computes SHA-256, verifies the
   signature with the pinned key, checks the bundle ID and code signature,
   then swaps the bundle with rollback.

Local signing:

```bash
./scripts/sign-release.sh dist/GateLink-arm64.dmg .secrets/gatelink-release.key
./scripts/sign-release.sh dist/GateLink-x64.dmg .secrets/gatelink-release.key
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

## macOS runner compatibility and the bundled engine

GitHub no longer offers macOS 14 runners (retired November 2, 2026), and a
Homebrew bottle poured on macOS 15/26 carries that OS as its `minos`. Copying
a Homebrew openconnect would therefore raise GateLink's real minimum above the
`LSMinimumSystemVersion` in `Info.plist`, producing `dyld: Symbol not found`
on macOS 14.

`scripts/build-openconnect-engine.sh` builds OpenSSL and OpenConnect from
pinned sources with `-mmacosx-version-min=14.0`, using OpenSSL instead of
GnuTLS so Homebrew's p11-kit/GnuTLS stack is not linked in. libxml2 comes from
the SDK. CI verifies every bundled Mach-O has `minos <= 14.0`.

The workflow uses `macos-15` (arm64) and `macos-15-intel` (x64). The runner
version no longer determines the engine's deployment target, so moving to
newer runners only changes the build toolchain; keep the `MAX=14.0` check.

macOS 12–13 still needs a separate build: lower `LSMinimumSystemVersion`, set
`TARGET` in `scripts/build-openconnect-engine.sh` (and `-target` in
`scripts/build-macos-app.sh`), and use an SDK that supports that target.

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
