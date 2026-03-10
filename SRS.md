SOFTWARE REQUIREMENTS SPECIFICATION
Aplikasi FO Testing & Commissioning
Sistem Pengujian & Sertifikasi Jaringan Fiber Optik
Desktop Application for Windows
Versi: 1.0.0
Tanggal: Maret 2026

 
1. Pendahuluan
1.1 Tujuan Dokumen
Dokumen Software Requirements Specification (SRS) ini mendeskripsikan secara lengkap kebutuhan fungsional dan non-fungsional untuk Aplikasi FO Testing & Commissioning. Aplikasi ini merupakan sistem desktop berbasis Windows yang digunakan oleh teknisi lapangan Jinom AI untuk melakukan pengujian koneksi jaringan fiber optik, validasi performa, dan pencatatan hasil sertifikasi pelanggan.

1.2 Ruang Lingkup Sistem
Aplikasi ini mencakup modul-modul utama berikut:
•	Manajemen data pelanggan dan lokasi instalasi
•	Eksekusi pengujian jaringan: Ping Gateway, Ping DNS, NSLookup, Ping Domain Lokal
•	Pengujian aplikasi: Browsing, Streaming, dan Social Media
•	Speedtest menggunakan Jinom Speedtest dan Ookla Speedtest
•	Penyimpanan hasil pengujian ke database terpusat
•	Pembuatan laporan hasil sertifikasi dalam format cetak/PDF
•	Dashboard monitoring riwayat pengujian

1.3 Definisi & Singkatan
Istilah	Definisi
FO	Fiber Optik - media transmisi data berbasis cahaya
ISP	Internet Service Provider - penyedia layanan internet
DNS	Domain Name System - sistem penerjemah nama domain ke IP
NSLookup	Name Server Lookup - utilitas untuk query DNS
RTO	Request Time Out - indikasi paket tidak mendapat respons
Ping	Packet Internet Groper - utilitas pengujian latensi jaringan
Mbps	Megabits per second - satuan kecepatan transfer data
SRS	Software Requirements Specification - dokumen kebutuhan perangkat lunak
Commissioning	Proses validasi dan penerimaan instalasi jaringan sebelum diserahkan ke pelanggan

1.4 Referensi
•	Desain UI: Screenshot halaman 'Hasil Pengujian' - devresellers.jinom.ai/sertifikasi/daftar/fo-testing-commissioning
•	Kode Frontend: Komponen React NetTest Pro (nettest-pro.tsx)
•	IEEE Std 830-1998: Recommended Practice for Software Requirements Specifications

 
2. Deskripsi Umum Sistem
2.1 Perspektif Produk
Aplikasi FO Testing & Commissioning adalah aplikasi desktop Windows yang berdiri sendiri (standalone) namun terhubung ke database server terpusat. Aplikasi ini menggantikan proses pencatatan manual hasil pengujian oleh teknisi lapangan dan menyediakan laporan terstandarisasi yang dapat dicetak atau diekspor sebagai bukti sertifikasi layanan kepada pelanggan.

Arsitektur sistem terdiri dari:
•	Client Layer: Aplikasi desktop Windows (.exe) pada perangkat teknisi
•	Network Layer: Koneksi ke jaringan lokal/internet untuk eksekusi pengujian
•	Data Layer: Database terpusat (SQL Server / PostgreSQL) untuk penyimpanan hasil
•	Reporting Layer: Modul generasi laporan PDF/cetak

2.2 Fungsi Utama Produk
Fungsi utama yang harus disediakan aplikasi:
1.	Input data identitas pelanggan dan detail instalasi
2.	Eksekusi otomatis paket pengujian jaringan lengkap
3.	Penampilan hasil pengujian secara real-time
4.	Penyimpanan hasil ke database dengan timestamp dan ID teknisi
5.	Generasi laporan sertifikasi dalam format standar perusahaan
6.	Riwayat dan pencarian data pengujian sebelumnya

2.3 Karakteristik Pengguna
Peran	Tanggung Jawab	Kebutuhan Akses
Teknisi Lapangan	Melakukan instalasi dan pengujian di lokasi pelanggan	Input data, eksekusi test, cetak laporan
Supervisor / QA	Memvalidasi hasil pengujian dan menyetujui commissioning	Baca semua data, approve/reject, export laporan
Admin Sistem	Manajemen pengguna, konfigurasi sistem, backup data	Akses penuh termasuk konfigurasi dan manajemen user

2.4 Asumsi & Ketergantungan
•	Perangkat teknisi menjalankan Windows 10/11 (64-bit)
•	Koneksi internet tersedia di lokasi untuk menjalankan pengujian
•	Database server dapat diakses melalui jaringan internal Jinom
•	Aplikasi speedtest eksternal (Ookla) dapat diakses dari lokasi instalasi

 
3. Kebutuhan Fungsional
3.1 Modul Login & Autentikasi
FR-001: Login Pengguna
•	Sistem menyediakan form login dengan field username dan password
•	Autentikasi dilakukan terhadap database pengguna terpusat
•	Sesi login disimpan selama aplikasi aktif
•	Sistem mencatat log aktivitas login (waktu, user, perangkat)

3.2 Modul Data Pelanggan
FR-010: Input Data Pelanggan
•	Teknisi mengisi form data identitas pelanggan sebelum memulai pengujian
•	Field wajib: Nama Pelanggan, Nomor Pelanggan, Alamat, Paket Layanan (Mbps)
•	Field opsional: Nomor Telepon, Email, Catatan Teknis
•	Sistem memvalidasi format data sebelum menyimpan

FR-011: Pencarian Data Pelanggan
•	Teknisi dapat mencari data pelanggan berdasarkan nama, nomor pelanggan, atau alamat
•	Sistem menampilkan riwayat pengujian sebelumnya untuk pelanggan yang sama

3.3 Modul Pengujian Jaringan
Seluruh pengujian dieksekusi secara otomatis dan berurutan setelah pengguna memulai sesi pengujian. Progres ditampilkan secara real-time.

FR-020: Ping Gateway ISP
Parameter	Keterangan
Target	Gateway router distribusi ISP
Jumlah Ping	100 kali (configurable)
Output	Latency Max, Min, Average, RTO count
Threshold Pass	Average <= 10ms, RTO = 0
Tujuan	Memastikan koneksi antara router distribusi dan POP utama Jinom berjalan baik

FR-021: Ping DNS Server
Parameter	Keterangan
Target DNS	DNS Jinom (103.122.65.66) - configurable
Jumlah Ping	100 kali
Output	Latency Max, Min, Average, RTO count
Threshold Pass	Average <= 15ms, RTO = 0
Tujuan	Memastikan akses ke DNS Server dengan stabil untuk layanan resolusi nama domain

FR-022: NSLookup Domain Nasional
•	Sistem melakukan NSLookup terhadap domain-domain nasional yang dikonfigurasi
•	Domain default: jinom.net, kompas.com
•	Output: Status resolusi (Resolved / Failed) untuk setiap domain
•	Tujuan: Memvalidasi konfigurasi DNS internal untuk domain lokal Indonesia

FR-023: NSLookup Domain Internasional
•	Sistem melakukan NSLookup terhadap domain-domain internasional yang dikonfigurasi
•	Domain default: google.com, facebook.com
•	Output: Status resolusi (Resolved / Failed) untuk setiap domain
•	Tujuan: Memverifikasi koneksi dan performa DNS menuju internet luar

FR-024: Ping Domain Lokal
Parameter	Keterangan
Target Default	detik.com (configurable)
Jumlah Ping	Sesuai konfigurasi (default: 10)
Output	Latency Max, Min, Average, RTO count, Target Domain
Tujuan	Menguji konektivitas dan respons jaringan ke server/layanan lokal Indonesia

3.4 Modul Testing Aplikasi
FR-030: Pengujian Browsing
Sistem mengakses URL yang dikonfigurasi dan mengukur waktu loading halaman:
•	URL Default: https://kompas.com, https://jinom.net
•	Output: Waktu akses dalam satuan detik untuk setiap URL
•	Threshold Pass: Waktu akses <= 10 detik

FR-031: Pengujian Streaming
Sistem memverifikasi aksesibilitas layanan streaming:
•	URL Default: https://youtube.com, https://tiktok.com
•	Output: Status aksesibilitas (Loaded / Failed)
•	Threshold Pass: Status = Loaded

FR-032: Pengujian Social Media
Sistem memverifikasi aksesibilitas platform media sosial:
•	URL Default: https://whatsapp.com, https://facebook.com
•	Output: Status aksesibilitas (Loaded / Failed)
•	Threshold Pass: Status = Loaded

3.5 Modul Speedtest
FR-040: Jinom Speedtest
•	Sistem menjalankan pengujian kecepatan menggunakan server speedtest Jinom
•	Output: Kecepatan Download (Mbps) dan Upload (Mbps)
•	Threshold Pass: Download >= 99% dari paket yang diterima pelanggan

FR-041: Ookla Speedtest
•	Sistem menjalankan pengujian kecepatan menggunakan layanan Ookla (speedtest.net)
•	Output: Kecepatan Download (Mbps) dan Upload (Mbps)
•	Digunakan sebagai pembanding hasil Jinom Speedtest

3.6 Modul Penyimpanan & Laporan
FR-050: Simpan Hasil ke Database
•	Semua hasil pengujian disimpan otomatis ke database setelah sesi selesai
•	Data yang disimpan: ID Pelanggan, ID Teknisi, Timestamp, semua nilai hasil pengujian, Status kelulusan per-test
•	Sistem menghasilkan ID Sertifikasi unik per sesi pengujian

FR-051: Generasi Laporan Hasil Pengujian
•	Sistem menghasilkan laporan PDF berformat standar perusahaan
•	Laporan memuat: Header perusahaan, data pelanggan, semua hasil pengujian, status kelulusan, tanda tangan teknisi, tanggal
•	Laporan dapat dicetak langsung dari aplikasi
•	Laporan dapat diekspor sebagai file PDF

FR-052: Riwayat Pengujian
•	Sistem menampilkan daftar riwayat pengujian yang dapat difilter berdasarkan tanggal, teknisi, dan status
•	Pengguna dapat membuka kembali detail hasil pengujian sebelumnya
•	Supervisor dapat mengunduh laporan dari riwayat mana pun

 
4. Kebutuhan Non-Fungsional
4.1 Performa
ID	Kriteria	Target
NF-01	Waktu Startup Aplikasi	<= 5 detik pada hardware standar
NF-02	Waktu Satu Sesi Pengujian Penuh	<= 5 menit (semua modul)
NF-03	Generasi Laporan PDF	<= 10 detik
NF-04	Respons UI	Tidak ada freeze selama eksekusi pengujian (async)

4.2 Keandalan
•	Aplikasi harus mampu beroperasi minimal 8 jam terus-menerus tanpa crash
•	Jika koneksi database terputus, aplikasi menyimpan hasil sementara secara lokal dan menyinkronkan saat koneksi pulih
•	Semua error ditangkap dan dicatat ke log dengan pesan yang informatif
•	Tidak ada kehilangan data hasil pengujian akibat kegagalan sistem

4.3 Keamanan
•	Autentikasi wajib sebelum akses ke fitur apapun
•	Password disimpan dalam format hash (bcrypt atau setara)
•	Koneksi ke database menggunakan enkripsi TLS/SSL
•	Akses fitur berdasarkan peran pengguna (Role-Based Access Control)
•	Sesi otomatis berakhir setelah 30 menit tidak aktif

4.4 Kemudahan Penggunaan (Usability)
•	Alur kerja satu pengujian selesai dalam <= 5 langkah interaksi pengguna
•	Semua status dan progres pengujian ditampilkan secara visual yang jelas
•	Terdapat panduan inline dan tooltip untuk setiap modul pengujian
•	Antarmuka menggunakan Bahasa Indonesia
•	Mendukung resolusi layar minimum 1366x768

4.5 Kompatibilitas
•	Platform: Windows 10 (build 1903+) dan Windows 11
•	Arsitektur: 64-bit
•	Framework: .NET 6+ atau Electron (sesuai stack yang dipilih)
•	Database: SQL Server 2019+ atau PostgreSQL 14+
•	Tidak memerlukan instalasi perangkat lunak tambahan oleh pengguna akhir

4.6 Pemeliharaan
•	Kode dikembangkan mengikuti standar clean code yang terdokumentasi
•	Konfigurasi DNS target, URL test, dan threshold tersimpan di file konfigurasi terpisah (tidak hardcoded)
•	Log aplikasi dirotasi setiap 7 hari, disimpan maksimal 30 hari
•	Pembaruan aplikasi dapat di-deploy tanpa uninstall manual

 
5. Desain Database
5.1 Skema Tabel Utama
Berikut adalah entitas-entitas database utama yang dibutuhkan sistem:

Tabel: users
Kolom	Tipe	Constraint	Keterangan
id	INT	PK, AUTO_INCREMENT	ID unik pengguna
username	VARCHAR(50)	UNIQUE, NOT NULL	Username login
password_hash	VARCHAR(255)	NOT NULL	Password terenkripsi
full_name	VARCHAR(100)	NOT NULL	Nama lengkap teknisi
role	ENUM	NOT NULL	technician / supervisor / admin
is_active	BOOLEAN	DEFAULT TRUE	Status aktif akun
created_at	TIMESTAMP	NOT NULL	Waktu pembuatan akun

Tabel: customers
Kolom	Tipe	Constraint	Keterangan
id	INT	PK, AUTO_INCREMENT	ID pelanggan
customer_number	VARCHAR(20)	UNIQUE, NOT NULL	Nomor pelanggan Jinom
full_name	VARCHAR(150)	NOT NULL	Nama lengkap pelanggan
address	TEXT	NOT NULL	Alamat instalasi
package_mbps	INT	NOT NULL	Paket layanan dalam Mbps
phone	VARCHAR(20)	NULL	Nomor telepon

Tabel: test_sessions
Kolom	Tipe	Constraint	Keterangan
id	INT	PK, AUTO_INCREMENT	ID sesi pengujian
certification_id	VARCHAR(30)	UNIQUE, NOT NULL	Nomor sertifikasi unik
customer_id	INT	FK -> customers	Referensi pelanggan
technician_id	INT	FK -> users	ID teknisi pelaksana
test_date	TIMESTAMP	NOT NULL	Waktu pelaksanaan test
overall_status	ENUM	NOT NULL	PASS / FAIL / PARTIAL
notes	TEXT	NULL	Catatan teknisi

Tabel: test_results
Kolom	Tipe	Constraint	Keterangan
id	INT	PK, AUTO_INCREMENT	ID hasil
session_id	INT	FK -> test_sessions	Referensi sesi
test_type	VARCHAR(50)	NOT NULL	ping_gateway, ping_dns, nslookup_nasional, dll.
target	VARCHAR(255)	NOT NULL	IP/domain yang diuji
result_json	JSON / TEXT	NOT NULL	Hasil lengkap dalam format JSON
status	ENUM	NOT NULL	PASS / FAIL

 
6. Desain Antarmuka Pengguna
6.1 Prinsip Desain
•	Layout: Sidebar navigasi kiri + area konten utama di kanan
•	Tema warna: Biru (#1E5AA0) sebagai warna utama, abu-abu muda sebagai latar
•	Ikon status: Hijau untuk PASS/berhasil, Merah/kuning untuk FAIL/perhatian
•	Tabel dan kartu dengan border tipis, shading header berwarna biru
•	Progress bar animasi ditampilkan saat pengujian sedang berjalan

6.2 Halaman Utama - Hasil Pengujian
Berdasarkan desain referensi (screenshot), halaman hasil pengujian menampilkan:

Seksi	Komponen yang Ditampilkan
Header Halaman	Judul 'Hasil Pengujian', deskripsi singkat, nama pelanggan
Ping Gateway ISP	Tabel: Ping Count, Max, Min, Average, RTO. Kolom kanan: penjelasan tujuan pengujian
Ping DNS Server	Tabel: Ping Count, Max, Min, Average, RTO, DNS yang diuji. Kolom kanan: penjelasan
NSLookup Nasional	Tabel: Domain vs Status (Resolved/Failed) per domain
NSLookup Internasional	Tabel: Domain vs Status per domain
Ping Domain Lokal	Tabel: Ping Count, Max, Min, Average, RTO, Target Domain
Testing Aplikasi	Browsing (URL + waktu), Streaming (URL + status), Social Media (URL + status)
Speedtest	Jinom Speedtest (DL/UL Mbps) + Ookla Speedtest (DL/UL Mbps). Catatan rekomendasi

6.3 Navigasi Aplikasi
Menu navigasi utama terdiri dari:
•	Dashboard - Ringkasan aktivitas dan statistik
•	Pengujian Baru - Form input pelanggan dan eksekusi test
•	Hasil Pengujian - Tampilan detail hasil sesi aktif
•	Riwayat - Daftar semua sesi pengujian sebelumnya
•	Laporan - Generasi dan download laporan PDF
•	Pengaturan - Konfigurasi DNS, URL target, threshold (khusus Admin)
•	Manajemen Pengguna - CRUD akun teknisi (khusus Admin)

 
7. Alur Kerja Utama (User Flow)
7.1 Alur Pengujian Standar
Langkah	Aksi Pengguna	Respons Sistem
1	Buka aplikasi dan login	Autentikasi, tampilkan Dashboard
2	Klik 'Pengujian Baru'	Tampilkan form input data pelanggan
3	Isi data pelanggan, klik 'Mulai Test'	Validasi form, simpan draft, mulai eksekusi pengujian
4	Menunggu (progres otomatis)	Eksekusi berurutan: Ping GW -> Ping DNS -> NSLookup -> Ping Domain -> App Test -> Speedtest
5	Lihat hasil yang tampil real-time	Tampilkan halaman 'Hasil Pengujian' dengan semua data
6	Klik 'Simpan & Cetak Laporan'	Simpan ke DB, generate PDF, tampilkan preview cetak
7	Cetak atau export PDF	Kirim ke printer / simpan file PDF lokal

 
8. Konfigurasi Sistem
8.1 Parameter yang Dapat Dikonfigurasi
Seluruh parameter berikut dapat diubah melalui halaman Pengaturan oleh Admin, tanpa perlu rebuild aplikasi:

Parameter	Default	Keterangan
DNS Target (Ping DNS)	103.122.65.66	IP DNS Server Jinom
Jumlah Ping Gateway	100	Jumlah iterasi ping
Jumlah Ping DNS	100	Jumlah iterasi ping DNS
Domain NSLookup Nasional	jinom.net, kompas.com	Daftar domain yang diuji
Domain NSLookup Internasional	google.com, facebook.com	Daftar domain internasional
Domain Ping Lokal	detik.com	Target ping domain lokal
URL Browsing Test	kompas.com, jinom.net	URL untuk pengujian browsing
URL Streaming Test	youtube.com, tiktok.com	URL untuk pengujian streaming
URL Social Media Test	whatsapp.com, facebook.com	URL untuk pengujian sosmed
Threshold Speedtest (%)	99	% minimal dari paket pelanggan
Timeout Sesi (menit)	30	Auto logout jika tidak aktif

 
9. Lingkungan Teknis & Infrastruktur
9.1 Spesifikasi Hardware Minimum
Komponen	Minimum	Rekomendasi
OS	Windows 10 64-bit (1903)	Windows 11 64-bit
Prosesor	Intel Core i3 / AMD Ryzen 3	Intel Core i5 / AMD Ryzen 5
RAM	4 GB	8 GB
Penyimpanan	200 MB ruang bebas	500 MB ruang bebas
Layar	1366 x 768	1920 x 1080
Koneksi	Ethernet / WiFi ke jaringan Jinom	Ethernet (lebih stabil)

9.2 Stack Teknologi yang Direkomendasikan
•	Frontend/UI: C# WPF (.NET 8) dengan Material Design atau Electron.js + React
•	Backend Logic: C# (.NET 8) atau Node.js
•	Database ORM: Entity Framework Core (untuk C#) atau Sequelize (untuk Node.js)
•	Database Server: PostgreSQL 14+ (disarankan) atau SQL Server 2019+
•	PDF Generation: iTextSharp (.NET) atau Puppeteer (Node.js)
•	Network Commands: System.Net.NetworkInformation (.NET) atau child_process (Node.js)

 
10. Matriks Kebutuhan & Prioritas
ID	Kebutuhan	Prioritas	Sprint	Status
FR-001	Login & Autentikasi	WAJIB	Sprint 1	Belum Dimulai
FR-010	Input Data Pelanggan	WAJIB	Sprint 1	Belum Dimulai
FR-011	Pencarian Data Pelanggan	TINGGI	Sprint 2	Belum Dimulai
FR-020	Ping Gateway ISP	WAJIB	Sprint 1	Belum Dimulai
FR-021	Ping DNS Server	WAJIB	Sprint 1	Belum Dimulai
FR-022	NSLookup Nasional	WAJIB	Sprint 1	Belum Dimulai
FR-023	NSLookup Internasional	WAJIB	Sprint 1	Belum Dimulai
FR-024	Ping Domain Lokal	WAJIB	Sprint 1	Belum Dimulai
FR-030	Pengujian Browsing	WAJIB	Sprint 2	Belum Dimulai
FR-031	Pengujian Streaming	WAJIB	Sprint 2	Belum Dimulai
FR-032	Pengujian Social Media	WAJIB	Sprint 2	Belum Dimulai
FR-040	Jinom Speedtest	WAJIB	Sprint 2	Belum Dimulai
FR-041	Ookla Speedtest	TINGGI	Sprint 2	Belum Dimulai
FR-050	Simpan Hasil ke Database	WAJIB	Sprint 2	Belum Dimulai
FR-051	Generasi Laporan PDF	WAJIB	Sprint 3	Belum Dimulai
FR-052	Riwayat Pengujian	TINGGI	Sprint 3	Belum Dimulai


--- Akhir Dokumen SRS ---
Jinom AI | FO Testing & Commissioning | v1.0.0 | Maret 2026
