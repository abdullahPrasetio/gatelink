# Menjalankan GateLink di macOS

Panduan ini untuk menjalankan GateLink (klien VPN GlobalProtect dengan
authenticator 2FA bawaan) di macOS. UI dibangun dengan Avalonia sehingga
berjalan native di Apple Silicon maupun Intel.

---

## 1. Prasyarat

| Prasyarat | Untuk apa | Cara pasang |
|-----------|-----------|-------------|
| **Homebrew** | package manager | lihat https://brew.sh |
| **.NET SDK 8 atau 9** | build & run aplikasi | `brew install dotnet` atau unduh dari Microsoft |
| **OpenConnect** | engine VPN sebenarnya | `brew install openconnect` |
| **macOS 15+** | Avalonia, .NET, dan bundled openconnect Homebrew | — |

> Catatan: `GateLink.Core` menargetkan `net8.0` dan `GateLink.Desktop` menargetkan
> `net9.0`. Bundled openconnect dibangun untuk macOS 15; untuk macOS 12–14, build
> openconnect dari source dengan `MACOSX_DEPLOYMENT_TARGET=12.0` atau gunakan
> instalasi Homebrew lokal yang kompatibel. Jika hanya punya satu dari keduanya, build tetap jalan
> karena runtime yang lebih baru kompatibel ke belakang.

### Cek cepat prasyarat

```bash
brew --version
dotnet --info | head -5          # pastikan ada SDK 8.x atau 9.x
openconnect --version            # mis. OpenConnect version v9.21
```

Jika `openconnect` belum ada:

```bash
brew install openconnect
```

---

## 2. Cara tercepat: build aplikasi .app (double-click)

Untuk memakai GateLink seperti aplikasi biasa (tanpa `dotnet run`, muncul di
menu bar tanpa ikon dock):

```bash
cd GateLink
./scripts/build-macos-app.sh
```

Hasilnya: `dist/GateLink.app`. Jalankan dengan:

```bash
open dist/GateLink.app
```

Atau drag `GateLink.app` ke folder **Applications** supaya permanen. Aplikasi
ini self-contained (membawa runtime .NET sendiri), jadi bisa dipakai di Mac lain
tanpa memasang .NET. Ikon GateLink akan muncul di menu bar kanan atas.

> Aktifkan koneksi penuh dengan memasang aturan sudoers (lihat bagian 5b)
> supaya tunnel bisa naik tanpa prompt password.

Akun 2FA dan password tersimpan permanen di **Keychain** macOS, jadi tidak
hilang saat aplikasi ditutup.

## 3. Alternatif: jalankan dari sumber (untuk development)

```bash
cd GateLink
dotnet restore
dotnet build
```

Jalankan aplikasinya:

```bash
dotnet run --project src/GateLink.Desktop
```

Jendela GateLink akan muncul: header dengan status koneksi, kartu kode OTP
6 digit yang berjalan dengan hitung mundur, dan panel **Add account**.

> Aplikasi saat ini membawa satu akun contoh ("Example VPN / demo-user") agar kode
> OTP langsung terlihat. Tambahkan akun aslimu lewat QR atau URI manual.

---

## 3. Menjalankan test (opsional, tanpa GUI)

Logika inti (TOTP, parser QR, vault) punya 32 unit test:

```bash
dotnet test tests/GateLink.Core.Tests
```

Jika hanya runtime 9 yang terpasang sementara proyek test menargetkan net8.0,
jalankan dengan roll-forward:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test tests/GateLink.Core.Tests
```

---

## 4. Menambahkan akun 2FA

**Lewat QR:** klik **Choose QR image…**, pilih file gambar QR (`otpauth://`).
**Manual:** tempel URI `otpauth://totp/...` atau kunci Base32 di kolom, klik **Add**.

---

## 5. Cara pakai (menu bar app)

Setelah `dotnet run`, ikon GateLink muncul di **menu bar kanan atas**. Klik
ikonnya untuk membuka popover; klik di luar untuk menyembunyikan. Menu kanan
(atau item di popover) menyediakan **Quit**.

Di popover: isi Username & Password, lalu klik **⚡ Connect**. OTP dihasilkan
otomatis oleh aplikasi (tidak perlu diketik). Tombol berubah jadi
**Disconnect** saat tunnel aktif.

## 5b. Mengaktifkan koneksi penuh (butuh sekali setup helper root)

Fase tunnel openconnect perlu root untuk membuat interface `utun`. GateLink
memanggil **satu wrapper root-owned** lewat `sudo -n`; sudoers tidak lagi
memberi NOPASSWD ke openconnect, `/bin/kill`, atau file milik user:

```bash
cd GateLink
./scripts/install-tunnel-helper.sh --user "$USER"
```

Skrip ini:
- menyalin openconnect, dylib, `vpnc-script`, dan `hipreport.sh` ke
  `/usr/local/libexec/gatelink/` sebagai `root:wheel`,
- memvalidasi rule dengan `visudo -c`,
- hanya mengizinkan perintah
  `/usr/local/libexec/gatelink/gatelink-tunnel` di `/etc/sudoers.d/gatelink`.

Wrapper hanya menerima `connect --host <host-dari-form> --fingerprint <pin>`,
`stop`, `status`, dan `version`; ia sendiri yang menyusun argumen openconnect.
Jadi caller tidak bisa menyuntikkan `--script`, `--csd-wrapper`, atau opsi lain.

Untuk membatalkan:

```bash
./scripts/install-tunnel-helper.sh --uninstall
```

> **Catatan keamanan.** Ini bukan NOPASSWD untuk binary serbaguna. Semua file
> yang dijalankan sebagai root dimiliki `root:wheel`, host berasal dari form
> dan divalidasi sebagai DNS/IP (tidak ada opsi/path/script dari caller), dan
> PID yang dihentikan diverifikasi milik openconnect. Residual risk:
> openconnect tetap memproses input jaringan sebagai root; itu melekat pada
> model VPN ini sampai pindah ke NetworkExtension.

## 6. Status integrasi macOS

Yang sudah berfungsi dan terverifikasi jalan di Mac:

- **Menu bar app** — ikon di kanan atas, popover muncul/sembunyi, tanpa jendela
  biasa. ✅
- **OTP otomatis** — kode 6 digit dihasilkan aplikasi, tampil dengan countdown. ✅
- **Connect tersambung ke openconnect** — tombol Connect menjalankan
  `VpnSession` → openconnect (fase authenticate dengan OTP otomatis). ✅

Elevasi tunnel (root-owned helper):

- **Terpasang via `scripts/install-tunnel-helper.sh`.** Setelah dijalankan
  sekali, tombol Connect dapat membawa tunnel naik tanpa prompt password.
  openconnect hanya dijalankan oleh wrapper root-owned dengan argumen tetap dan
  script root-owned; stop dilakukan lewat wrapper yang memverifikasi PID.

- **Vault terenkripsi** — seed TOTP + password VPN disimpan dalam file
  AES-256-GCM; kunci 32-byte ada di Keychain dengan ACL yang hanya memercayai
  app. Membaca kunci meminta persetujuan/Touch ID. Metadata akun (issuer,
  account, digits, period) tetap plaintext agar daftar bisa dimuat tanpa
  prompt; seed/password tidak pernah ada di metadata. Item Keychain lama
  dimigrasikan sekali. ✅

> Saat macOS menanyakan akses Keychain untuk kunci vault, pilih **Allow**
> (bukan **Always Allow**) agar `/usr/bin/security` tidak diberi izin permanen
> membaca kunci tersebut.
- **Touch ID** — prompt user-presence dari Keychain saat kunci vault dibaca
  (Touch ID atau password login). Satu unlock berlaku selama window, lalu key
  dihapus dari memori. ✅
- **Aplikasi .app** — `scripts/build-macos-app.sh` menghasilkan
  `dist/GateLink.app` yang self-contained, `LSUIElement=true` (tanpa ikon dock),
  ber-ikon, dan bisa double-click. ✅

Catatan jujur tentang yang belum diverifikasi penuh:

- **Handshake VPN sungguhan belum diuji** dengan kredensial asli — itu perlu
  akun VPN gateway-mu dan akan membuat koneksi jaringan nyata. Build, logika, dan
  peluncuran aplikasi sudah terbukti; koneksi aktual perlu kamu coba sendiri.
- **Prompt user-presence Keychain** — kunci vault dibaca lewat `/usr/bin/security`
  dengan ACL yang mempercayai app; prompt nyata (Touch ID atau password login)
  perlu dikonfirmasi di Mac. Migrasi item Keychain lama berjalan otomatis saat
  pertama kali; buat backup Keychain dulu jika ingin aman.
- **Windows** — DPAPI + Windows Hello sudah di-compile untuk target Windows,
  tetapi belum dijalankan di mesin Windows dalam pengujian ini.

Semua logika VPN & OTP ada di `GateLink.Core` (82 unit test lulus).
