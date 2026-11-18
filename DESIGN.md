# Kontrak Desain: Jinom Test (WPF)

Jinom Test memakai **Material Design 3** dengan token yang sama dengan jinom-assets (`DESIGN.md` dan `resources/css/app.css` di sana, seed `#10B981`). Teknisi yang berpindah dari web ke aplikasi ini harus melihat warna, bentuk, tipografi dan bahasa status yang sama.

Kontrak ini **dijaga oleh tes**: `FoTestingApp.Tests/UiContractTests.cs` (jalan di OS apa pun, `dotnet test`). Tes gagal berarti ada pelanggaran. Jangan longgarkan tesnya; perbaiki kodenya.

---

## 1. Prinsip

* **Data dulu, dekorasi belakangan.** Tanpa gradien, tanpa animasi hiasan, tanpa emoji. Ikon dari `PackIcon` (Material Design Icons).
* **Tanpa bayangan**, kecuali elemen yang benar-benar melayang: FAB, dialog, snackbar, menu. Kartu dipisahkan dengan garis `OutlineVariant` atau warna `SurfaceContainer*`.
* **Status tidak pernah hanya warna.** Setiap status tampil sebagai ikon + kata (`PASS`, `PARTIAL`, `FAIL`), lewat `Ui.StatusChip` / `StatusVisual`.
* **Jujur soal data.** Gagal memuat ditulis sebagai pesan, bukan angka `0` atau daftar kosong. Daftar yang dipotong server diberi tahu.
* **Teks UI Bahasa Indonesia**, istilah teknis tetap Inggris (Ping, DNS, NSLookup, Speedtest, Gateway, PASS/FAIL).

## 2. Struktur file tema

| File | Isi |
| :--- | :--- |
| `Themes/Tokens.Light.xaml`, `Themes/Tokens.Dark.xaml` | Peran warna M3 (`{Peran}Color` + `{Peran}Brush`) dan override brush MaterialDesignInXAML. Kedua file **wajib punya key yang sama** (dites). |
| `Themes/Typography.xaml` | Skala tipe `Text.*` dan `AppFont`. |
| `Themes/Controls.xaml` | Bentuk `Shape.*`, tombol `M3.*`, chip, nav item, kartu, divider. |
| `App.xaml` | Urutan merge (CustomColorTheme → MaterialDesign3.Defaults → Tokens.Light → Controls) dan style turunan MDIX: `M3.TextField`, `M3.PasswordField`, `M3.ComboField`, `M3.DateField`, `M3.LinearProgress`, `M3.DataGrid`. |
| `Helpers/ThemeService.cs` | Ganti terang/gelap/ikuti Windows: menukar kamus `Tokens.*` + `PaletteHelper`. Pilihan disimpan per pengguna (`%LOCALAPPDATA%\JinomTest\preferences.json`). |
| `Helpers/Ui.cs` | `StatusVisual`, `Ui.Text`, `Ui.StatusChip`, `Ui.ResultRow`, `Ui.Notify` (snackbar), `Ui.ConfirmAsync` / `Ui.AlertAsync` (dialog M3). |

> Style berbasis MDIX harus di `App.xaml`, bukan di kamus terpisah: `StaticResource` di kamus yang di-merge terpisah tidak melihat kamus saudara (MDIX), dan baru gagal saat aplikasi jalan.

## 3. Warna

Selalu lewat token, **tidak pernah hex di Views** (dites). Di XAML pakai `{DynamicResource XBrush}` agar ikut berganti tema; di C# pakai `SetResourceReference(..., "XBrush")`, bukan `Brushes.*` / `Color.FromRgb` (dites; `Brushes.Transparent` untuk hit-test boleh).

| Peran | Pakai untuk |
| :--- | :--- |
| `Primary` / `OnPrimary` | Tombol utama (satu per layar), elemen aktif. Terang `#006C49`, gelap `#4EDEA3`. |
| `PrimaryContainer`, `SecondaryContainer` (+ `On…`) | Nav item aktif, tombol tonal, avatar, sorotan lembut. |
| `Surface`, `SurfaceContainerLowest…Highest` | Latar halaman, drawer (`Low`), dialog (`High`), kartu terisi. |
| `OnSurface` / `OnSurfaceVariant` | Teks utama / teks pendukung & ikon sekunder. |
| `Outline` / `OutlineVariant` | Garis field / garis kartu & divider. |
| `Error`, `ErrorContainer` (+ `On…`) | Validasi, kegagalan aksi, tombol destruktif. |
| `StatusOnline*` / `StatusWarning*` / `StatusOffline*` / `StatusUnknown*` | PASS / PARTIAL / FAIL / belum diketahui. Masing-masing ada aksen, `Container`, `OnContainer`. Hanya untuk status, bukan dekorasi. |
| `Info*`, `Scrim`, `InverseSurface` | Pemberitahuan netral, overlay dialog, snackbar. |

Laporan PDF (`Services/ReportService.cs`, kelas `Brand`) memakai nilai token **terang** yang sama.

## 4. Tipografi

Tidak ada `FontSize` / `FontFamily` literal di Views (dites); pakai style:

| Style | Ukuran | Contoh |
| :--- | :--- | :--- |
| `Text.DisplaySmall` | 32/40 | Skor hasil pengujian |
| `Text.HeadlineSmall` | 24/32 | Judul halaman, angka metrik |
| `Text.TitleLarge` / `Text.TitleMedium` | 22/28, 16/24 | Judul panel / judul kartu |
| `Text.BodyLarge` / `BodyMedium` / `BodySmall` | 16, 14, 12 | Isi |
| `Text.LabelLarge` / `Medium` / `Small` | 14, 12, 11 | Tombol, chip, label tabel |
| `Text.Supporting` | 14, `OnSurfaceVariant` | Keterangan di bawah judul/field |
| `Text.FieldError` | 12, `Error` | Pesan validasi di bawah field |

## 5. Bentuk & spasi

* Sudut: field 4 (`Shape.Field`), chip 8, kartu 12, FAB 16, dialog/sheet 28, tombol & nav item pil (`Shape.Pill`).
* Kelipatan 4/8: margin halaman 24, padding kartu 16–24, jarak antar field 24 (ruang untuk helper/error text), jarak antar kartu 16.
* Lebar konten form & hasil maks 840; jendela utama bisa di-resize (min 1024×680); jendela login 440×560 tetap.

## 6. Komponen

* **Tombol** (`Controls.xaml`): `M3.FilledButton` untuk aksi utama (satu per layar/dialog), `M3.TonalButton`, `M3.OutlinedButton` untuk aksi kedua, `M3.TextButton` untuk aksi ringan/batal, `M3.ErrorTextButton` untuk destruktif, `M3.IconButton` (40×40, wajib `ToolTip` + `AutomationProperties.Name`), `M3.ExtendedFab` hanya "Pengujian baru" di Riwayat.
* **Field**: `M3.TextField` / `M3.PasswordField` / `M3.ComboField` / `M3.DateField` (outlined, floating hint). Label statis di atas field dilarang; pakai `HintAssist.Hint`, keterangan lewat `HintAssist.HelperText`, satuan lewat `TextFieldAssist.SuffixText`. Field salah: garis `ErrorBrush` + `Text.FieldError` di bawahnya.
* **Pilihan**: `M3.FilterChip` (RadioButton) untuk filter & pilihan pendek, `M3.ChoiceCard` untuk pilihan besar (POP vs Pelanggan).
* **Navigasi**: drawer 280 px di `MainShell` dengan `M3.NavItem` (Pengujian baru, Riwayat), tombol tema, akun & keluar di bawah. Laporan ada di Riwayat (unduh per baris, dan di hasil pengujian); tidak ada halaman Pengaturan: parameter diatur di Jinom Assets (Integrasi → Jinom Test).
* **Login**: hanya logo, dua field dan tombol "Masuk"; teks lain hanya alasan gagal masuk.
* **Kartu**: `M3.Card` (garis) untuk kelompok konten, `M3.FilledCard` untuk metrik/ringkasan.
* **Dialog & pesan**: `Ui.ConfirmAsync` (konfirmasi; `destructive: true` untuk keluar/membatalkan pengujian), `Ui.AlertAsync` (penjelasan yang harus dibaca), `Ui.Notify` (hasil aksi, hilang sendiri). `MessageBox` dilarang di Views (dites).
* **Hasil uji**: `Ui.ResultRow` (ikon jenis uji, judul, ringkasan, chip status); dikelompokkan dalam kartu yang bisa dibuka per grup.

## 7. Tata letak per halaman

* **Halaman data (Riwayat)**: Grid `Auto/Auto/Auto/*` — judul, metrik, filter tetap di atas; `DataGrid` mengisi sisa ruang dan scroll sendiri. **Jangan** taruh `DataGrid` di dalam `ScrollViewer` (mematikan virtualisasi, filter ikut tergulung). Detail sesi di panel kanan 400 px (list-detail).
* **Pengujian berjalan**: Grid tanpa scroll halaman; stepper, aktivitas sekarang, lalu log yang mengisi sisa tinggi (scroll internal).
* **Form & hasil**: `ScrollViewer` + konten maks 840 px, aksi utama di kanan bawah.

## 8. Visualisasi data (`Controls/Charts`)

Grafik digambar sendiri (`OnRender`, tanpa library) di atas `ChartBase`: warna hanya dari token (`Token("PrimaryBrush")`) dan digambar ulang saat tema berganti, teks 11/12 px, tooltip polos M3 (`InverseSurface`) saat hover. Data grafik dibaca lewat `Services/Core/ChartData.cs` (dites) dari data hasil yang sama dengan baris dan PDF.

| Kontrol | Dipakai di | Isi |
| :--- | :--- | :--- |
| `LatencyChart` | Pengujian berjalan (live), detail hasil ping | RTT tiap echo (garis 2 px, putus di RTO), RTO ditandai merah di tepi atas, batas lulus putus-putus; hover = echo ke-n + ms. |
| `BulletChart` | Hasil pengujian, panel detail Riwayat | Satu baris per target: batang warna status, garis batas, nilai + "gagal" (status tidak hanya warna). |
| `SessionTrendChart` | Riwayat | Sesi per hari (per minggu bila > 9 minggu) ditumpuk PASS/PARTIAL/FAIL, celah 2 px, ujung atas membulat; hover = jumlah; klik = filter ke hari itu. Legenda ikon + kata di sampingnya. |
| `ScoreRing` | Hasil pengujian | Lulus/total sebagai cincin; angka "7/9 lulus" di tengah. |

Aturan: satu sumbu Y per grafik; warna status hanya untuk status; nilai yang tidak terukur (RTO, 0 Mbps, 9999 lama) tidak pernah digambar sebagai angka; setiap grafik punya `AutomationProperties.Name`.

## 9. Ikon & aksesibilitas

* Ikon aplikasi = ikon Jinom Assets (`Resources/jinom.ico`, 16–256 px dari `public/images/logo.png` assets); logo dan ikon didaftarkan sebagai `<Resource>` di csproj (tanpa itu `pack://` kosong saat jalan).
* Nama ikon harus ada di `PackIconKind` versi MDIX yang dipakai (dites; nama salah baru gagal saat jalan).
* Kontrol tanpa teks wajib `AutomationProperties.Name` dan `ToolTip`.
* Enter di login lewat `IsDefault` tombol, bukan handler `KeyDown` (mencegah login ganda).
* Periksa setiap perubahan di **mode terang dan gelap** di Windows.
