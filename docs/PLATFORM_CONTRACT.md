# Kontrak Platform Jinom: peran dan batas tiap sistem

**Berlaku sejak 12 November 2026.**

Berkas ini sama persis di enam repo: jinom-assets, jinom-nms, jinom-vpn, jinom-radius, jinom-test,
dan jinom-cacti-bridge (masing-masing di `docs/PLATFORM_CONTRACT.md`). Ubah di keenamnya sekaligus.

Dokumen ini menjawab tiga pertanyaan untuk setiap sistem: **apa tugasnya, siapa yang memakainya,
dan siapa yang boleh ia panggil**. Ia berada di atas kontrak yang sudah ada:

| Pertanyaan | Dokumen yang memutuskan |
| --- | --- |
| Peran sistem, siapa boleh memanggil siapa | dokumen ini |
| Siapa pemilik sebuah data, lewat jalur apa salinannya ditulis | `DATA_OWNERSHIP.md` (jinom-assets, jinom-nms, jinom-vpn) |
| Bentuk request, respons, dan webhook assets ↔ NMS | `JINOM_NMS_INTEGRATION_CONTRACT.md` (jinom-assets, jinom-nms), `DEVICE_CONNECTIVITY_CONTRACT.md` (jinom-assets) |
| Alur login PPPoE, isolir, dan RADIUS | `RADIUS_ARCHITECTURE.md` (jinom-assets) |

Bila sebuah perubahan melanggar dokumen ini, yang diubah lebih dulu adalah dokumen ini — dengan
persetujuan — bukan kodenya diam-diam.

## 1. Sistem dan perannya

| Sistem | Peran | Dipakai langsung oleh |
| --- | --- | --- |
| **Jinom Panel** | Sumber data pelanggan dan administrasi mitra/reseller/company: siapa mitranya, siapa pelanggannya, paket, tagihan, keputusan isolir. | Staf Jinom |
| **jinom-assets** (Jinom Infra) | Platform tempat pengguna menginput, mengelola, dan memantau perangkat serta jaringan. Satu-satunya pintu bagi mitra. | Mitra/reseller, staf Jinom |
| **jinom-nms** | Backend NMS: berkomunikasi dengan perangkat, melakukan polling, menyimpan status, telemetri, dan alert, menjalankan diagnosis. | jinom-assets (dua pengecualian di §4) |
| **jinom-vpn** | Mengelola tunnel dan namespace jaringan untuk setiap mitra, supaya perangkat mitra terjangkau. | jinom-nms |
| **jinom-radius** | Menangani komunikasi RADIUS: login dan accounting PPPoE/Hotspot, Disconnect/CoA. | jinom-assets; perangkat BRAS mitra lewat protokol RADIUS |
| **jinom-test** | Aplikasi desktop teknisi untuk menguji koneksi di POP dan di pelanggan, lalu mencatat hasil sertifikasinya. | Teknisi lapangan |
| **jinom-cacti-bridge** | Pembaca grafik Cacti (hanya baca) untuk jinom-assets. | jinom-assets |

## 2. Aturan

**A1. Satu pintu untuk mitra.** Pengguna mitra hanya berinteraksi dengan jinom-assets. Tidak ada
sistem lain yang punya akun mitra, halaman untuk mitra, atau dipanggil langsung oleh browser mitra.

**A2. jinom-nms adalah backend.** Ia memproses semua kebutuhan NMS dan tidak menjadi tempat kerja
pengguna. Tampilan web yang masih ada padanya adalah pengecualian yang tercatat di §4.

**A3. Hanya jinom-nms yang berbicara ke perangkat.** SNMP, SSH/Telnet, API RouterOS, dan TR-069
berjalan dari jinom-nms. jinom-assets tidak pernah membuka koneksi ke perangkat; ia meminta
jinom-nms melakukannya. Satu-satunya jalur lain ke perangkat adalah protokol RADIUS antara BRAS dan
jinom-radius.

**A4. Semua kemampuan jinom-nms bisa dipakai jinom-assets.** Setiap kemampuan NMS tersedia sebagai
API lebih dulu. Tampilan apa pun di atas NMS — termasuk yang ada di jinom-nms sendiri — memakai API
yang sama, tidak punya jalur khusus. Kemampuan baru belum selesai sebelum jinom-assets bisa
memakainya. Yang belum memenuhi aturan ini didaftar di §5.

**A5. jinom-vpn satu-satunya pengelola tunnel.** Tunnel, namespace, dan alamat tunnel dibuat dan
diubah hanya oleh jinom-vpn. jinom-nms adalah satu-satunya yang memanggil API-nya. jinom-assets
menampilkan keadaan tunnel lewat jinom-nms.

**A6. jinom-radius satu-satunya yang berbicara RADIUS.** Akun, NAS, dan profil dimiliki
jinom-assets dan dikirim ke jinom-radius; tidak ada sistem lain yang menulis ke jinom-radius.
Keputusan isolir datang dari Jinom Panel ke jinom-assets, lalu jinom-assets yang menerapkannya.

**A7. Jinom Panel adalah sumber data pelanggan dan administrasi mitra.** jinom-assets menyimpan
salinannya dan tidak mengubah data itu. Sistem lain mendapatkannya dari jinom-assets, bukan
langsung dari Panel.

**A8. Batas mitra ditegakkan oleh sistem penerima.** Identitas mitra antar sistem adalah
`companies.id` milik jinom-assets. Setiap permintaan yang menyangkut satu mitra membawanya, dan
sistem yang menerima menolak data di luar mitra itu.

**A9. Kredensial perangkat hanya mengalir ke jinom-nms.** Username, password, dan community
perangkat dimiliki jinom-assets dan hanya dikirim ke jinom-nms lewat izin khusus. Tidak pernah ke
browser, ke jinom-test, atau ke sistem lain.

**A10. Tidak ada yang membaca database sistem lain.** Data berpindah lewat API pemiliknya, webhook
dari pemiliknya, atau tarikan dari API pemiliknya. Ini berlaku juga untuk jinom-test: ia hanya
lewat API jinom-assets.

**A11. Jujur saat sistem lain mati.** Bila jinom-nms, jinom-vpn, jinom-radius, atau Panel tidak
menjawab, jinom-assets menampilkan data terakhir beserta waktunya dan mengatakan sumbernya tidak
terjangkau. Ia tidak menampilkan status yang ditebak.

**A12. Antarmuka berubah tanpa memutus.** Deploy antar sistem tidak serentak, jadi perubahan API
antar sistem tetap menerima bentuk lama selama satu rilis, dan dokumen bentuknya diubah di kedua
repo sekaligus. API sertifikasi reseller yang dipakai jinom-test dan mitra sudah dibekukan: hanya
boleh ditambah, tidak diubah.

## 3. Siapa boleh memanggil siapa

Panggilan yang tidak ada di tabel ini tidak boleh dibuat tanpa mengubah dokumen ini.

| Pemanggil | Yang dipanggil | Untuk apa | Otorisasi |
| --- | --- | --- | --- |
| Browser mitra dan staf | jinom-assets | Semua pekerjaan pengguna | Sesi login jinom-assets |
| jinom-assets | jinom-nms | Status, telemetri, alert, diagnosis, aksi ke perangkat, kirim inventaris | API key NMS + `X-Company-ID` |
| jinom-nms | jinom-assets | Tarik inventaris dan daftar company; webhook status, ACS, profil mitra | Token API dengan izin kredensial; webhook bertanda tangan |
| jinom-nms | jinom-vpn | Tunnel, namespace, riwayat dan metrik tunnel | API key jinom-vpn |
| jinom-nms | Perangkat mitra | SNMP, SSH/Telnet, API RouterOS, TR-069 | Kredensial dari jinom-assets, lewat tunnel jinom-vpn |
| jinom-assets | jinom-radius | Kirim akun/NAS/profil; baca sesi dan percobaan login | Token radius-api, jaringan privat |
| Perangkat BRAS mitra | jinom-radius | Login dan accounting RADIUS | Secret RADIUS per NAS |
| jinom-radius | Perangkat BRAS mitra | Disconnect/CoA | Secret RADIUS per NAS |
| Jinom Panel | jinom-assets | Perintah isolir/buka isolir | Token khusus Panel |
| jinom-assets | Jinom Panel | Tarik mitra dan pelanggan; baca status tagihan | Token API Panel |
| jinom-test | jinom-assets | Login teknisi, data pelanggan untuk uji, simpan hasil uji | API sertifikasi reseller (beku) |
| jinom-assets | jinom-cacti-bridge | Daftar dan nilai grafik Cacti | Bearer token bridge |

## 4. Pengecualian yang berlaku

| Pengecualian | Kenapa masih ada | Sampai kapan |
| --- | --- | --- |
| **Meja CS** (diagnosis dan gangguan pelanggan) berjalan di tampilan web jinom-nms dan dipakai staf internal. | Belum ada platform tersendiri untuk CS. | Sampai platform Meja CS dibuat. Platform itu berdiri sendiri, tidak digabung ke jinom-nms maupun jinom-assets, dan memakai API jinom-nms dengan aturan yang sama dengan jinom-assets (A4, A8). |

| **Layar NOC** di tampilan web jinom-nms. | Alat uji: dipakai untuk mencoba kemampuan jinom-nms lewat GUI sebelum kemampuan itu diteruskan ke jinom-assets. | Tetap. Ia bukan tempat kerja pengguna: sebuah kemampuan dicoba di sini, lalu dibuat tersedia di jinom-assets. |

Untuk kedua pengecualian: tampilan web jinom-nms hanya untuk staf internal, tidak pernah untuk
mitra (A1), dan hanya memakai API yang juga bisa dipakai jinom-assets (A4). Sebuah kemampuan yang
hanya bisa dicapai lewat layar NOC belum selesai.

## 5. Yang belum memenuhi A4

Daftar awal, dari yang diketahui saat dokumen ini ditulis. Belum diperiksa satu per satu terhadap
seluruh halaman dan API jinom-nms.

| Kemampuan jinom-nms | Keadaan di jinom-assets |
| --- | --- |
| Diagnosis pelanggan dan runbook, papan gangguan | Belum ada; hanya di Meja CS (§4) |
| Pengelolaan ACS secara massal (preset, firmware) | Belum ada; baru per ONT |
| Pengelolaan aturan alert | Belum ada. Daftar alert sendiri sudah ada: halaman Problem Monitoring dan tab Alert di GIS Maps |

Perlu diperiksa apakah sudah setara: akses web jarak jauh ke perangkat, pengelolaan driver dan
profil SNMP, halaman tunnel, dan pengaturan sistem jinom-nms.
