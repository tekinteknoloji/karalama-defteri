# Karalama Defteri değişiklik günlüğü

## Android 0.5.1 — 17.11.2026

- Tam genişlikte arama, sol üstten açılan grup çekmecesi ve sağ altta yüzen yeni not düğmesi.
- Boş grupta tek yeni not düğmesi. Kartlarda sağ üstte sabitleme ve renk işaretleri.
- Ekran genişliğine göre iki, üç veya dört sütun. Durum ve gezinme çubukları uygulama temasına bağlı.

## Android 0.5.0 — 17.11.2026

- Android yayın sertifikasıyla imzalanan ilk APK.
- Uygulama içinden Android sürümünü kontrol etme, APK’yı indirme ve doğrulama, Android kurulum onayıyla veri koruyan güncelleme.
- Android paketleri için ayrı GitHub sürüm etiketleri.

## Android 0.4.0 — 17.11.2026

- Android paylaşım menüsü, kamera, hızlı not widget’ı ve simge kısayolu.
- Pano, yerel şifreleme, TOTP Doğrulayıcı ve isteğe bağlı biyometrik kilit.

## Android 0.3.0 — 16.11.2026

- Windows ile ortak Google Drive veri biçimi. Canlı Android Drive kabulü bekliyor.

## Android 0.2.0 — 16.11.2026

- Not kartları, grup çekmecesi, arama, otomatik kayıtlı editör, resim görüntüleyici ve açık/koyu/sistem temaları.

## Android 0.1.0 — 16.11.2026

- Android 15 üzerinde ortak veritabanı, şifreleme ve resim işleme denemesi.

## 2.3.3 — 16.11.2026

- Dağıtım paketine Google Drive masaüstü OAuth yapılandırması eklendi. Google izniyle bağlanmak için ayrı OAuth dosyası gerekmiyor.
- Uygulama ana sayfası ve gizlilik politikası bağlantıları tekinteknoloji.com adreslerine taşındı.

## 2.3.2 — 14.11.2026

- Bulut eşitlemesi arka planda çalışıyor. Ağ kesintisinde tamamlanan kayıtlar korunuyor ve eşitleme sonraki turda devam ediyor.

## 2.3.1 — 14.11.2026

- Ayarlar kart düzeni ve güncelleme ilerlemesi iyileştirildi.
- Açık ve koyu temada metin seçimi okunur hale getirildi.
- Windows Hello anahtar oluşturma ve kilit açma WinRT üzerinden çalışıyor.

## 2.3.0 — 14.11.2026

- Ayarlar → Özellikler bölümünden modüller ayrı ayrı açılıp kapatılabilir.
- Pano, grup başına sütunlar ve fare veya klavyeyle kart sıralama sunuyor. Sütunlar ve kart sırası şifreli eşitlemeyle taşınıyor.
- Son tarih ve hatırlatma kartlara yaklaşan ve geciken tarih renklerini, tarih filtrelerini ve isteğe bağlı Windows bildirimini ekliyor.

## 2.2.5 — 14.11.2026

- Google Drive ve OneDrive oturumu saklanıyor. Bağlı kopyalar yeniden açıldığında arka planda eşitleniyor.
- Bulut eşitlemesi değişiklikten sonra, saatlik veya günlük çalışabiliyor. Son yedi günlük şifreli yedek tutuluyor.
- Bulut parolası uyuşmazlığı için açıklama ve yedekli sıfırlama eklendi. İkinci kopyada çöp kutusundaki güncel notlar ve ekleri aktarılıyor.

## 2.2.4 — 14.11.2026

- Google Drive, portable klasördeki yerel masaüstü OAuth dosyasıyla etkinleştirilebilir.

## 2.2.3 — 14.11.2026

- Yeni sürüm bulunduğunda önceki güncelleme mesajı temizleniyor. Kanal değişikliğinde güncelleme durumu sıfırlanıyor ve notların Geri al bildirimi korunuyor.

## 2.2.2 — 14.11.2026

- Ayarlar bölümleri sol tarafta dikey bir listede gösteriliyor.
- Klasör ve bulut seçenekleri Senkron sayfasında birleşti. Yapılandırılmamış sağlayıcılar pasif olarak gösteriliyor.

## 2.2.1 — 14.11.2026

- Kenar çubuğu Görünümler, Gruplar ve Araçlar bölümlerine ayrıldı. Arşiv ve Çöp kutusu altta sabit kaldı. Satırlarda ok tuşlarıyla gezilebilir ve Enter ile seçim yapılabilir.
- Gruplara ayır seçeneği, üst çubuktaki Görünüm menüsüne taşındı.

## 2.2.0 — 14.11.2026

- Hakkında ve Ctrl+K paletine gönüllü destek seçenekleri eklendi. Ödeme düğmeleri yapılandırılınca görünür. 30 gün ve 100 not aşıldığında alt bilgide bir kez destek notu gösterilir.

## 2.1.1 — 14.11.2026

- Google Authenticator’ın 2. sürüm aktarım QR biçimi destekleniyor. Telefonun ürettiği QR ile içe aktarma ve görüntülenen TOTP kodu doğrulandı.

## 2.1.0 — 14.11.2026

- Yerel şifreleme açıkken kullanılabilen Doğrulayıcı eklendi. TOTP ve HOTP hesapları QR, bağlantı veya elle eklenebilir. Google Authenticator’ın çok parçalı QR aktarımı desteklenir.
- Doğrulayıcı hesapları firmaya ve nota bağlanabilir. Ctrl+K paletinde `kod:` ile aranabilir. Kopyalanan kod, hâlâ panodaysa 30 saniye sonra temizlenir.
- GitHub Releases tabanlı güncelleme, SHA-256 doğrulaması, isteğe bağlı imzalama ve başarısız ilk açılışta önceki EXE’ye dönüş eklendi.

## 2.0.0 — 14.11.2026

- Faz 2 sürümünde yerel yedekleme, not aktarımı, masaüstü notları, resim düzenleme, etiketler ve yapılacaklar listesi bir araya getirildi.
- SQLCipher yerel şifreleme, 24 kelimelik kurtarma ve şifreli yedekten geri yükleme eklendi. Windows Hello PIN testi ertelendi.
- Şifreli klasör senkronu arka planda çalışır. İki kopyada aynı not düzenlendiğinde son sürüm ve diğer düzenlemeyi içeren çakışma kopyası korunur.
- Google Drive ve OneDrive OAuth akışları sahte sunucuyla doğrulandı. iCloud için CloudKit sağlayıcı iskeleti eklendi. Üç sağlayıcının canlı testleri bekliyor.

## 2.0.0-beta — 14.11.2026

- SQLCipher ile yerel şifreleme, şifreli yedekler ve 24 kelimelik kurtarma eklendi.
- Kilit ekranı global kısayolları bekletir ve başarılı açılıştan sonra uygular.
- Windows Hello için makineye bağlı anahtarla açma seçeneği eklendi.

## 1.9.0 — 14.11.2026

- Kontrol merkezi ve resim görüntüleyicinin araç çubukları sadeleştirildi.
- Ctrl+K paleti notları, grupları ve Türkçe komutları arıyor. Yalnızca komutları görmek için sorguyu `>` ile başlatabilirsiniz.

## 1.8.0 — 14.11.2026

- Etiketler, birleşik arama filtreleri ve işaretlenebilir not listeleri eklendi.
- Kart ve liste görünümü, otomatik arşiv ve satır içi resim boyutlandırma eklendi.

## 1.7.0 — 14.11.2026

- Yerel yedekleme, güvenlik yedeğiyle geri yükleme ve not aktarımı eklendi.
- Notlar ayrı masaüstü pencerelerine iğnelenebiliyor.
- Resim görüntüleyiciye işaretleme, kırpma ve orijinali geri yükleme eklendi.

## 1.6.1 — 14.11.2026

- Dağıtım EXE’si .NET çalışma zamanı ve uygulama dosyalarını içeren tek dosyalı paket olarak yeniden oluşturuldu.

## 1.6.0 — 14.11.2026

- Editördeki grup seçicinin iç çerçeveleri kaldırıldı.
- Renk filtresi yeniden adlandırıldı ve editöre not rengi seçici eklendi.
- Windows genel kısayolları açılıp kapatılabiliyor, yeniden atanabiliyor ve varsayılana döndürülebiliyor.

## 1.5.1 — 13.11.2026

- İkinci açılış mevcut pencerenin yanıtını bekliyor. Yanıt gelmezse pencereyi yeniden açmak için izlenecek adımları gösteriyor.

## 1.5.0 — 13.11.2026

- Ayarlar’a Windows geneli, uygulama ve editör kısayollarını açıklamalarıyla gösteren Kısayollar sekmesi eklendi.

## 1.4.1 — 13.11.2026

- Tamamen saydam pano görüntüleri eleniyor ve sonraki pano biçimi deneniyor. Saydam küçük önizlemelerde kart, orijinal resmi kullanmayı deniyor.

## 1.4.0 — 13.11.2026

- Kontrol merkezinin sol altına tıklanabilir sürüm ve derleme zamanı eklendi.
- Hakkında penceresine **Sürümü kopyala** düğmesi eklendi. Tepsi ipucu sürümü gösteriyor.

## 1.3.2

- ShareX’in **Kopyala → Dosya** seçeneğiyle panoya konan resim dosyaları editöre yapıştırılabiliyor.

## 1.3.1

- Panodan PNG ve DIB okuma eklendi. Alfa kanalı tamamen sıfır olan eski ekran görüntüleri açılırken görünür hale getiriliyor.
- Satır içi resimlere açık boyut verildi ve eklemeden sonra editör yeniden çiziliyor.

## 1.3.0

- Veri, arama, sözdizimi ve resim işleme WPF’ten bağımsız Core kütüphanesine taşındı.
- SQLite migrasyonu UUIDv7, milisaniyeli UTC güncelleme zamanı, cihaz kimliği ve 90 günlük kalıcı silme kayıtlarını ekledi.
- Renk filtresine görünür etiket eklendi.

## 1.2.0

- Açık ve koyu tema paletleri, özel kontrol şablonları ve temaya bağlı editör ve başlık çubukları eklendi.
- Tekin Teknoloji’nin site ikonu exe, pencere, tepsi ve Hakkında’da kullanılmaya başlandı.

## 1.1.0

- Taşınabilir `Veri/notes.db` düzeni ve Tekin Teknoloji marka öğeleri eklendi.
- Firma grupları, `@grup` araması ve notlara resim ekleme geldi.
- Tek dosyalık exe sıkıştırıldı.

## 1.0.0

- İlk Faz 1 yayını. Otomatik kayıt, sekmeli editör, Türkçe tam metin araması, not kartları, arşiv, çöp kutusu, tepsi ve global kısayollar.

