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
| **macOS 12+** | Avalonia & .NET | — |

> Catatan: proyek `GateLink.Core` menargetkan `net8.0` dan `GateLink.Desktop`
> menargetkan `net9.0`. Jika hanya punya satu dari keduanya, build tetap jalan
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

> Aplikasi saat ini membawa satu akun contoh ("BRI VPN / demo-user") agar kode
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

## 5b. Mengaktifkan koneksi penuh (butuh sekali setup root)

Fase tunnel openconnect perlu root untuk membuat interface `utun`. GateLink
memanggil openconnect lewat `sudo -n` (non-interaktif), jadi kamu perlu memasang
aturan sudoers NOPASSWD sekali:

```bash
cd GateLink
./scripts/install-sudoers.sh
```

Skrip ini:
- mendeteksi lokasi `openconnect`,
- memvalidasi aturan dengan `visudo -c` sebelum memasang (tidak akan merusak
  sudoers sistem),
- memasang `/etc/sudoers.d/gatelink` (minta password sudo kamu sekali).

Untuk membatalkan:

```bash
./scripts/install-sudoers.sh uninstall
```

> **Peringatan keamanan.** NOPASSWD untuk openconnect secara efektif membuka
> jalur menjalankan kode sebagai root bagi siapa pun yang bisa bertindak sebagai
> user-mu (openconnect bisa menjalankan script). Pasang hanya di mesin yang kamu
> percaya. Ini kelemahan yang sama seperti aplikasi asli MantraVPN
> (`/etc/sudoers.d/mantra2fa-openconnect`).

## 6. Status integrasi macOS

Yang sudah berfungsi dan terverifikasi jalan di Mac:

- **Menu bar app** — ikon di kanan atas, popover muncul/sembunyi, tanpa jendela
  biasa. ✅
- **OTP otomatis** — kode 6 digit dihasilkan aplikasi, tampil dengan countdown. ✅
- **Connect tersambung ke openconnect** — tombol Connect menjalankan
  `VpnSession` → openconnect (fase authenticate dengan OTP otomatis). ✅

Elevasi tunnel (Opsi A — sudoers NOPASSWD):

- **Terpasang via `scripts/install-sudoers.sh`.** Setelah dijalankan sekali,
  tombol Connect dapat membawa tunnel naik tanpa prompt password. openconnect
  dipanggil lewat `sudo -n` dan dihentikan lewat pid file dengan `sudo kill`.
  Lihat peringatan keamanan di bagian 5b.

- **Keychain** — akun 2FA & password tersimpan permanen dan terenkripsi di
  Keychain login (via `/usr/bin/security`). Round-trip terverifikasi. ✅
- **Touch ID** — gate biometrik via `LocalAuthentication` (menjalankan snippet
  Swift). Jika Swift tidak tersedia, fallback ke prompt otorisasi sistem
  (Touch ID atau password). ✅
- **Aplikasi .app** — `scripts/build-macos-app.sh` menghasilkan
  `dist/GateLink.app` yang self-contained, `LSUIElement=true` (tanpa ikon dock),
  ber-ikon, dan bisa double-click. ✅

Catatan jujur tentang yang belum diverifikasi penuh:

- **Handshake VPN sungguhan belum diuji** dengan kredensial asli — itu perlu
  akun VPN BRI-mu dan akan membuat koneksi jaringan nyata. Build, logika, dan
  peluncuran aplikasi sudah terbukti; koneksi aktual perlu kamu coba sendiri.
- **Touch ID via Swift** diuji hanya jalur kompilasinya; prompt biometrik nyata
  perlu kamu konfirmasi di Mac dengan Touch ID.

Semua logika VPN & OTP ada di `GateLink.Core` (32 unit test lulus).
