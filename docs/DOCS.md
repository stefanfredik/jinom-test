Cara Build Aplikasi FO Testing
Prasyarat (di Windows)
Visual Studio 2022 (Community/Pro/Enterprise) — pastikan workload .NET desktop development terinstall
Atau dotnet SDK 8.0 saja jika mau build via command line
Opsi 1 — Via Visual Studio 2022 (Paling Mudah)
Copy folder jinom-testing/FoTestingApp/ ke Windows
Update appsettings.json sesuai environment:
json
"Database": {
  "Host": "10.70.103.56",   // IP server MySQL OpenAccess
  "Port": 3311,
  "Database": "openaccess",
  "Username": "sail",
  "Password": "password"
},
"NetworkTest": {
  "PingGateway": {
    "Target": "10.70.103.1"  // IP gateway ISP yang akan di-ping
  }
}
Buka FoTestingApp.csproj di Visual Studio 2022
Visual Studio otomatis restore semua NuGet packages
Tekan F5 → build + run langsung
Opsi 2 — Via Command Line (dotnet CLI)
powershell
# Development run (debug)
cd FoTestingApp
dotnet run
# Build release
dotnet build -c Release
# Build installer-ready (self-contained, tidak butuh .NET terinstall di target)
dotnet publish -c Release -r win-x64 --self-contained true -o publish
Opsi 3 — Buat Installer .exe (Distribusi ke Teknisi)
Setelah dotnet publish berhasil:

Install Inno Setup 6 dari jrsoftware.org/isdl.php
Buka FoTestingApp/installer/FoTestingApp.iss
Klik Build → Compile (atau tekan F9)
Installer tersimpan sebagai installer/FoTestingApp-v1.0.0-Setup.exe
Installer ini bisa langsung dikopi dan dijalankan di laptop teknisi tanpa perlu install .NET atau dependency apapun
Troubleshooting Umum
Error	Solusi
MySqlConnector connection failed	Cek IP/port DB di appsettings.json dan pastikan firewall MySQL mengizinkan koneksi
BCrypt mismatch	Password OpenAccess menggunakan 12 rounds — sudah dikonfigurasi dengan benar
QuestPDF license error	Sudah diset ke LicenseType.Community di ReportService
Vite manifest error	Tidak relevan (ini khusus Laravel)
Catatan: Aplikasi ini tidak bisa dibuild di Linux karena WPF adalah teknologi Windows-only. Gunakan Windows atau Windows VM untuk build.