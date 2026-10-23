# GateLink on Windows

The Windows client shares all its logic and UI with macOS (one Avalonia
codebase + `GateLink.Core`). The platform differences are handled automatically.

## Getting a build (no Windows machine needed)

Every push to `main` triggers a GitHub Actions workflow
(`.github/workflows/build.yml`) that:

1. runs the unit tests (Linux),
2. builds a **self-contained Windows `.exe`** (`GateLink.exe`), and
3. builds the macOS `.app` bundle,

then uploads both as downloadable **artifacts**.

To get the Windows build:
1. Push the repo to GitHub.
2. Open the **Actions** tab → latest run → **Artifacts** →
   download `GateLink-windows-x64`.
3. Unzip and run `GateLink.exe`.

## Prerequisites on the Windows machine

| Prerequisite | Why | How |
|--------------|-----|-----|
| Windows 10/11 x64 | runtime | — |
| **OpenConnect for Windows** + Wintun driver | the VPN engine | [OpenConnect-GUI release](https://github.com/openconnect/openconnect-gui/releases) bundles both, or install `openconnect.exe` on PATH |
| Run **as Administrator** | create the virtual adapter | the app manifest already requests elevation |

GateLink finds `openconnect.exe` on PATH or in the common install folders.

## How Windows differs from macOS (handled in code)

| Concern | macOS | Windows |
|---------|-------|---------|
| Credential input | PTY via `expect` | stdin pipe, `--passwd-on-stdin` (password+OTP combined) |
| Elevation | `sudo` + sudoers | run as Administrator (manifest); Windows Service later |
| Secret storage | Keychain (`security`) | in-memory for now; DPAPI planned |
| Biometrics | Touch ID | stub for now; Windows Hello planned |
| Open at Login | LaunchAgent | not yet (registry/Startup planned) |
| HIP report | `hipreport.sh` via `--csd-wrapper` | not wired (shell script is POSIX) |

The connection flow itself (gateway login with `--usergroup=gateway`, combined
password+OTP, cookie parsing) is identical and lives in `GateLink.Core`.

## Build locally on Windows (optional)

```powershell
dotnet publish src\GateLink.Desktop\GateLink.Desktop.csproj `
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
  -o publish\win-x64
```

Run `publish\win-x64\GateLink.exe` as Administrator.

## Known gaps on Windows

- Secret storage, biometrics, Open-at-Login, and HIP report are macOS-only so
  far. On Windows the account/OTP work and the tunnel can connect when run
  elevated with openconnect on PATH, but password persistence and HIP
  submission still need Windows-specific implementations.
- The tray single-click-to-open behaviour uses a menu on both platforms.
