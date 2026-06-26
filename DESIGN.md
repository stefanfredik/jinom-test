# Kontrak Sistem Desain & Tata Letak: Jinom Test (WPF App)

Dokumen ini mendefinisikan standar visual, dimensi komponen, skema warna, dan aturan tata letak untuk aplikasi **Jinom Test**. Semua modifikasi, penambahan halaman baru, atau perubahan komponen UI harus mematuhi aturan (*contracts*) di bawah ini guna menjamin konsistensi, estetika modern (*Simple, Clean, & Eye-Catching*), serta meminimalkan usaha scrolling pengguna (*less scrolling effort*).

---

## 1. Filosofi Desain
* **Simple**: Menghilangkan elemen visual yang tidak diperlukan. Fokus pada data dan tindakan penting.
* **Clean**: Spacing yang teratur, perataan (*alignment*) yang konsisten, dan pemisahan area menggunakan bayangan (*drop shadow*) halus serta border tipis.
* **Eye-Catching**: Aksen hijau Jinom murni yang memikat untuk tombol utama, animasi mikro (seperti kedip-kedip log dot), dan kontras warna yang tajam baik di mode terang maupun gelap.

---

## 2. Palet Warna & Brush
Gunakan resource warna yang terdaftar di `App.xaml` (dan pastikan kompatibilitas mode terang/gelap):

| Nama Resource | Kegunaan | Warna Mode Terang | Warna Mode Gelap |
| :--- | :--- | :--- | :--- |
| `PrimaryBrush` | Warna aksen utama (Jinom Green) | `#22C55E` | `#22C55E` |
| `PrimaryDarkBrush` | Aksen ketika di-hover atau aktif | `#16A34A` | `#16A34A` |
| `PrimaryLightBrush` | Background aksen tipis (badge, hover) | `#DCFCE7` | `#24C55E` (Opacity 23% / Alpha 60) |
| `AppBackgroundBrush` | Latar belakang halaman utama | `#F8F6F6` | `#1E1E1E` |
| `CardBackgroundBrush` | Latar belakang panel kartu / border | `#FFFFFF` | `#2D2D30` |
| `BorderLightBrush` | Border tipis pembatas elemen | `#E2E8F0` | `#464646` |
| `TextDarkBrush` | Teks utama / judul (sangat terbaca) | `#0F172A` | `#F0F0F0` |
| `TextLightBrush` | Teks sekunder / keterangan / label | `#64748B` | `#A0A0A0` |
| `PassGreenBrush` | Indikator sukses / status PASS | `#10B981` | `#10B981` |
| `FailRedBrush` | Indikator error / status FAIL / tombol keluar | `#EF4444` | `#EF4444` |
| `WarnYellowBrush` | Indikator peringatan / status PARTIAL | `#F59E0B` | `#F59E0B` |

> [!IMPORTANT]
> **Aturan Mode Gelap**: Jangan menggunakan warna *hardcoded* (seperti `Black` atau `White`) untuk latar belakang atau tulisan di XAML. Selalu gunakan `{DynamicResource ...}` agar secara otomatis beralih saat mode gelap diaktifkan lewat `BtnToggleTheme_Click` di `MainShell.xaml.cs`.

---

## 3. Tipografi & Ukuran Teks
Untuk konsistensi visual, batasi variasi ukuran font ke skala berikut:

* **Page Title (Judul Halaman)**: `FontSize="24"`, `FontWeight="Bold"` atau `Black` (Margin Bottom: `8` atau `12`).
* **Section / Card Header**: `FontSize="16"` atau `18"`, `FontWeight="Bold"`.
* **Field Labels (Label Input)**: Ditiadakan (dihapus). Wajib menggunakan **Floating Hint** (`materialDesign:HintAssist.IsFloating="True"`) pada masing-masing control input untuk mengurangi visual noise (*clutter*) dan menghemat ruang vertikal.
* **Body / Normal Text**: `FontSize="13"` atau `14"`.
* **Captions / Small Status**: `FontSize="11"` atau `12"`.

---

## 4. Standar Dimensi & Kontrak Komponen (Form Sizing)

Untuk menyelaraskan tampilan UI dan meminimalkan scrolling, dimensi input dan tombol harus seragam:

### A. Jendela Utama (Window)
* Semua jendela menggunakan ukuran fixed tanpa kemampuan resize manual (kecuali minimize).
* **MainShell Window**: `Width="1280"`, `Height="768"`, `ResizeMode="CanMinimize"`.
* **LoginWindow**: `Width="1200"`, `Height="700"`, `ResizeMode="CanMinimize"`.

### B. Formulir & Input
* **Tinggi Input (Height)**: **Harus tepat `44px`** untuk semua `TextBox`, `ComboBox`, `PasswordBox`, dan `DatePicker` standard.
* **Padding Konten Input**: **Wajib menggunakan `Padding="12,8"`** pada semua input (`TextBox`, `PasswordBox`, `ComboBox`, dan `DatePicker`) yang bertinggi `44px`. Padding vertikal bawaan Material Design terlalu tebal dan akan menyebabkan tulisan terpotong (*clipping*) di dalam form.
* **Label & Floating Hint**: **Dilarang menggunakan TextBlock label statis di atas kolom input**. Semua input wajib menggunakan **Floating Hint** (`materialDesign:HintAssist.IsFloating="True"`) dengan properti `materialDesign:HintAssist.Hint` yang deskriptif. Jika kode-behind mendesak memerlukan variabel label, buat elemen label tersebut bertipe hidden/collapsed (`Visibility="Collapsed"`).
* **Desain Dropdown (ComboBox)**: Wajib menggunakan style `Style="{StaticResource JinomComboBoxStyle}"` untuk memastikan keseragaman tinggi `44px`, padding `12,8`, teks `FontSize="13"`, perataan vertikal terpusat, dan **minimalisasi efek bayangan** dengan properti `materialDesign:ShadowAssist.ShadowDepth="Depth1"`.
* **Item Dropdown (ComboBoxItem)**: Menggunakan gaya implisit (tanpa x:Key) dengan `FontSize="13"`, `Padding="12,8"`, teks `TextDarkBrush`, latar belakang default transparan, dan transisi warna dinamis saat hover/seleksi.
* **Tinggi Tombol Aksi (Button Height)**: **Harus tepat `44px`** untuk semua tombol halaman (Login, Cetak, Mulai Pengujian, Simpan).
* **Sudut Lengkung (Corner Radius)**:
  * Tombol standar menggunakan **`CornerRadius="8"`** (`materialDesign:ButtonAssist.CornerRadius="8"`).
  * Panel kartu utama menggunakan **`CornerRadius="12"`** atau **`CornerRadius="16"`**.
* **Spasi Antar Elemen (Spacing)**:
  * Margin bawah kolom input formulir: **`12px`** s.d. **`14px`** (misal: `Margin="0,0,0,12"`).
  * Padding dalam kartu formulir: **`20px`** (atau `16px` untuk panel pengaturan yang lebih padat).
  * Margin luar halaman dari tepi window: **`20px`** (atau `24,20,24,20`).

### C. Tabel Data (DataGrid)
* Tinggi baris data (`RowHeight`): **`44px`** s.d. **`48px`** agar mudah dibaca namun tetap menampung banyak data.
* Header tabel (`DataGridColumnHeader`): `FontSize="12"` atau `13`, `FontWeight="SemiBold"`, `Padding="16,10"`.
* Sel tabel (`DataGridCell`): `Padding="16,0"`, dipastikan konten di dalamnya berada di tengah secara vertikal (`VerticalAlignment="Center"`).

---

## 5. Aturan Minimalisasi Scrolling (Viewport & Scrolling Contract)

Untuk menghemat tenaga klik dan scroll teknisi (*minimize user effort*), terapkan prinsip berikut:

### A. Batasi Ketinggian Vertikal Pengujian
Halaman pengujian aktif (`NewTestPage` progress) harus termuat seluruhnya dalam ruang vertikal jendela `MainShell` (**tinggi bersih area kerja ~696px**):
* **Stepper Progress**: Tinggi lingkaran stepper maksimal `40px` dengan teks di bawahnya.
* **Terminal Log Console**: Tinggi area log scroll viewer **maksimal `180px`** (`LogsScrollViewer Height="180"`).
* **Margin Vertikal Kontainer**: Gunakan margin atas/bawah `12px` s.d. `16px` saja untuk progress container.
* Gabungan seluruh elemen (Header, Stepper, Current Task Card, Log Console, Cancel Button) **tidak boleh melebihi `620px`** agar terhindar dari munculnya scrollbar halaman.

### B. Aturan Tata Letak Grid untuk Halaman Data (Dashboard & Riwayat)
* **DILARANG** membungkus tabel data besar (`DataGrid`) di dalam layout scrollable secara penuh (`ScrollViewer` + `StackPanel`). Hal ini menonaktifkan virtualisasi WPF dan membuat seluruh filter serta header halaman tergulung hilang saat pengguna melakukan scroll.
* **KONTRAK LAYOUT**: Gunakan pembagian Grid terstruktur:
  ```xml
  <Grid Margin="20">
      <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/> <!-- Row 0: Header Halaman & Counter Status -->
          <RowDefinition Height="Auto"/> <!-- Row 1: Baris Filter / Pencarian -->
          <RowDefinition Height="*"/>    <!-- Row 2: Tabel Riwayat Utama (Mengisi sisa ruang) -->
      </Grid.RowDefinitions>
      
      <!-- Konten Row 0 & 1 Pinned/Sticky di atas -->
      
      <!-- Sessions Table Row 2 -->
      <Border Grid.Row="2" ...>
          <DataGrid ... /> <!-- Scroll secara internal menggunakan scrollbar vertikal bawaan grid -->
      </Border>
  </Grid>
  ```
  Ini mengunci filter pencarian dan statistik counter agar selalu terlihat, serta membatasi scrolling hanya terjadi secara internal di dalam tabel data.
