# GateLink

A macOS menu-bar and Windows tray client for **GlobalProtect** VPN gateways, built on top of
[OpenConnect](https://www.infradead.org/openconnect/). GateLink automates the
GlobalProtect two-factor authentication flow (password + OTP), obtains the WebVPN
session cookie, then launches `openconnect` to establish the tunnel.

This is an independent, clean-room client for macOS and Windows. It implements
its own UI and process orchestration in C#/.NET; the VPN protocol itself is
handled by the open-source OpenConnect binary.

## Status

Early scaffold. The connection flow mirrors the standard GlobalProtect
auth sequence:

```
openconnect --protocol=gp --authenticate <gateway>   ->  parse cookie
openconnect --protocol=gp --cookie-on-stdin --servercert <fp> <gateway>
```

## Architecture

| Layer            | Component                        | Responsibility                                |
|------------------|----------------------------------|-----------------------------------------------|
| UI               | `App` (WPF) + `TrayIcon`         | System tray menu, connect/disconnect, status  |
| Session          | `VpnSession`                     | State machine: idle → authenticating → connecting → connected |
| Auth             | `GlobalProtectAuthenticator`     | Runs `--authenticate`, parses `GpCookie`      |
| Process          | `OpenConnectRunner`              | Launches/stops openconnect, streams output    |
| Config           | `VpnEnvironment`                 | Resolves openconnect path, runtime dirs, logs |
| OTP              | `TotpGenerator` / `OtpAuthUri`   | Generates the 2FA code itself (RFC 6238)      |
| Setup            | `QrDecoder` / `OtpAuthUri`       | Add account via QR image or manual otpauth URI |
| Vault            | `OtpVault` + `IBiometricGate`    | Biometric unlock with sliding auto-relock     |
| Privilege (todo) | Windows Service + Wintun         | Elevated tunnel setup                         |

## Key behaviours

- **No manual OTP on connect.** The app stores the TOTP secret and generates the
  6-digit code itself, so connecting is one tap.
- **Add accounts two ways.** Scan a QR image (`otpauth://` payload) or paste the
  URI / enter the Base32 key manually. Both paths converge on `OtpAuthUri`.
- **Biometric vault (Touch ID / Windows Hello).** macOS stores the vault key in
  the login Keychain behind an app-only ACL and prompts for user presence when
  the key is read; Windows uses Windows Hello plus DPAPI. One unlock opens the
  vault for a sliding window, then cached key material is dropped.

`GateLink.Core` is platform-neutral (`net8.0`) and fully unit-tested on any OS
(88 tests, including the RFC 6238 TOTP vectors, QR round-trips, vault encryption and release signatures). Only the UI
layer is platform-specific.

## Requirements

- macOS 14+ on Apple Silicon (`GateLink-arm64.dmg`) or Intel (`GateLink-x64.dmg`)
- Windows 10/11 x64 (the app requests administrator elevation for the adapter)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) to build
- OpenConnect for Windows on PATH (or bundled) with the Wintun driver

## Build

```powershell
dotnet build
dotnet run --project src/GateLink.Desktop
```

## Troubleshooting

**"Root tunnel helper missing or not authorized" on macOS.** Check
`~/gatelink-tunnel.log`. GateLink now uses a root-owned, fixed-purpose helper at
`/usr/local/libexec/gatelink/gatelink-tunnel`; sudoers authorizes only that
wrapper, never openconnect or `/bin/kill`. Install it once with:

```bash
./scripts/install-tunnel-helper.sh --user "$USER"
```

Or press Connect and approve the native admin prompt. The wrapper only runs the
bundled OpenConnect with fixed options; the host comes from the Portal field. Remove it
with `./scripts/install-tunnel-helper.sh --uninstall`.

## Gateway

No gateway is hard-coded and none is stored by the installer. Enter the
portal/gateway in the UI form on every launch; the root-owned helper validates
it as a hostname and runs the bundled OpenConnect with fixed arguments.

The one-time helper install needs no gateway:

```bash
./scripts/install-tunnel-helper.sh --user "$USER"
```
