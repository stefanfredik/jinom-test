# Panduan Build — FO Testing & Commissioning

**Stack:** C# / WPF · .NET 8 · Windows-only  
**Versi:** 1.0.0 · Jinom AI

> **⚠️ Penting:** Aplikasi ini menggunakan WPF yang merupakan teknologi **Windows-only**.
> Build **tidak bisa** dilakukan di Linux/macOS. Gunakan mesin Windows atau Windows VM.

---

## Prasyarat

| Kebutuhan | Versi minimum | Keterangan |
|---|---|---|
| Windows | 10 build 17763 (1809) atau lebih baru | Target OS untuk build & run |
| .NET SDK | 8.0 | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Visual Studio | 2022 (opsional) | Workload **.NET desktop development** wajib aktif |
| Inno Setup | 6.x | Hanya diperlukan untuk membuat installer `.exe` |

---

## Langkah 1 — Konfigurasi `appsettings.json`

Sebelum build, sesuaikan `FoTestingApp/appsettings.json` dengan environment:

```json
{
  "OpenAccessApiUrl": "https://infra.jinom.net/api/v1",
  "NetworkTest": {
    "PingGateway": {
      "Target": "10.X.X.1"
    }
  },
  "Speedtest": {
    "JinomServer": "https://speedtest.jinom.net",
    "ThresholdPercentage": 85
  }
}
```

> `PingGateway.Target` harus diisi dengan IP gateway ISP yang akan diuji (kosong = ping dilewati).

---

## Langkah 2 — Build Aplikasi

### ⭐ Opsi A — Single-file `.exe` (Direkomendasikan untuk distribusi)

Menghasilkan **satu file `.exe` tunggal** yang langsung bisa dikopi dan dijalankan di PC manapun tanpa install .NET:

```powershell
cd FoTestingApp

dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:CopyOutputSymbolsToPublishDirectory=false `
  -o ..\dist
```

Output: `dist\FoTestingApp.exe` (~100–150 MB, sudah termasuk seluruh .NET runtime)

### Opsi B — Folder publish (lebih kecil, butuh folder utuh)

```powershell
cd FoTestingApp

dotnet publish -c Release -r win-x64 --self-contained true -o ..\publish
```

Seluruh isi folder `publish\` harus dikopi bersama-sama (tidak bisa hanya `.exe`-nya saja).

### Opsi C — Via Visual Studio 2022

1. Buka `FoTestingApp/FoTestingApp.csproj` di Visual Studio 2022
2. Visual Studio otomatis me-restore semua NuGet packages
3. Tekan **F5** untuk debug, atau **Ctrl+Shift+B** untuk build release
4. Untuk publish: **Build → Publish → Folder** lalu konfigurasi sesuai opsi A/B di atas

### Opsi D — Development (debug lokal)

```powershell
cd FoTestingApp
dotnet run
```

---

## Langkah 3 — Buat Installer `.exe` (Distribusi ke Teknisi)

> **Prasyarat:** Langkah 2 (`dotnet publish`) harus selesai terlebih dahulu.

1. Install **Inno Setup 6** dari [jrsoftware.org/isdl.php](https://jrsoftware.org/isdl.php)
2. Buka file `FoTestingApp/installer/FoTestingApp.iss` di Inno Setup Compiler
3. Klik **Build → Compile** (atau tekan **F9**)
4. Installer tersimpan di:
   ```
   FoTestingApp/Installer/FoTestingApp-v1.0.0-Setup.exe
   ```

Installer ini bisa langsung dikopi dan dijalankan di laptop teknisi **tanpa perlu install .NET** atau dependency apapun.

---

## Troubleshooting

| Error | Penyebab | Solusi |
|---|---|---|
| `API connection failed` | `OpenAccessApiUrl` salah atau server tidak bisa dijangkau | Cek URL di `appsettings.json` dan koneksi jaringan |
| `QuestPDF license error` | Versi QuestPDF Community | Sudah dikonfigurasi `LicenseType.Community` di `ReportService.cs` |
| `PingGateway.Target` kosong | Field target ping tidak diisi | Isi IP gateway ISP di `appsettings.json` |
| `Build failed: WPF not supported` | Build dilakukan di Linux/macOS | Pindah ke mesin Windows |
| `dotnet: command not found` | .NET SDK belum terinstall | Install dari [dot.net/download](https://dot.net/download) |

---

## Struktur Output

```
publish/                        ← hasil dotnet publish
  FoTestingApp.exe              ← executable utama
  appsettings.json              ← config (bisa diedit tanpa rebuild)
  *.dll                         ← runtime & dependency

FoTestingApp/Installer/
  FoTestingApp-v1.0.0-Setup.exe ← installer siap distribusi
```