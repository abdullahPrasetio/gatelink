# GateLink

A Windows tray client for **GlobalProtect** VPN gateways, built on top of
[OpenConnect](https://www.infradead.org/openconnect/). GateLink automates the
GlobalProtect two-factor authentication flow (password + OTP), obtains the WebVPN
session cookie, then launches `openconnect` to establish the tunnel.

This is an independent, clean-room Windows client. It implements its own UI and
process orchestration in C#/.NET; the VPN protocol itself is handled by the
open-source OpenConnect binary.

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
- **Biometric vault (Touch ID / Windows Hello).** One unlock opens the vault for
  a sliding window; connect and reveal/copy all proceed without re-prompting
  until auto-relock, mirroring the original app's BiometricGate.

`GateLink.Core` is platform-neutral (`net8.0`) and fully unit-tested on any OS
(32 tests, including the RFC 6238 TOTP vectors and QR round-trips). Only the UI
layer is platform-specific.

## Requirements

- Windows 10/11 x64
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- OpenConnect for Windows on PATH (or bundled) with the Wintun driver

## Build

```powershell
dotnet build
dotnet run --project src/GateLink.App
```

## Gateway

The default gateway is configured in `src/GateLink.Core/VpnConfig.cs`.
