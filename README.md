# Karalama Defteri

**Windows için Türkçe, taşınabilir not defteri.** Müşteri ve proje notlarını firmalara göre ayırın, ekran görüntülerini notların içine ekleyin ve aradığınızı Ctrl+K ile bulun. Yazdıklarınız otomatik kaydedilir.

[Son sürümü indir](https://github.com/tekinteknoloji/karalama-defteri/releases/latest) · [Sürüm notları](https://github.com/tekinteknoloji/karalama-defteri/releases)

## İlk kullanım

1. GitHub Releases sayfasında **Assets** bölümünden **KaralamaDefteri.exe** dosyasını indirin.
2. EXE’yi yazılabilir bir klasöre koyup çift tıklayın. Windows 10/11 x64 için kurulum veya ayrı .NET yüklemesi gerekmez.
3. **Yeni not** ile başlayın. Uygulama ilk açılışta EXE’nin yanına **Veri** klasörünü oluşturur.
4. Pencerenin **X** düğmesi uygulamayı sistem tepsisine alır. Tamamen kapatmak için tepsi menüsünde **Çıkış**’ı seçin.

## Notlar, gruplar ve arama

| Yapmak istediğiniz | İzlenecek yol |
| --- | --- |
| Firmaya göre düzenlemek | Kenar çubuğunda **+ Yeni grup** ile firma ekleyin. Kartı gruba sürükleyin veya editörün grup kutusunu kullanın. |
| Konuya göre etiketlemek | Nota `#vpn` veya `#yazıcı` gibi etiketler yazın. Bir not bir firmaya bağlanabilir ve birden fazla etiket taşıyabilir. |
| Bir firmada aramak | Aramaya `@` yazıp firmayı seçin, ardından aradığınız kelimeyi girin. Örnek: `@güven vpn`. |
| Not, grup veya komut bulmak | **Ctrl+K** paletini açın. Yalnızca komutları aramak için sorguya `>` ile başlayın. |
| Görünümü düzenlemek | Kart menüsünden bir notu sabitleyin, arşivleyin veya masaüstüne iğneleyin. Silinen notlar **Çöp kutusu** bölümünden geri alınabilir. |
| Yapılacaklar listesi oluşturmak | Not menüsünde **Listeye çevir** seçeneğini kullanın. |

### Ekran görüntüsü ve editör

**Win+Shift+S** ile alıntı alın ve editörde **Ctrl+V** ile yapıştırın. Panoda hem metin hem resim varsa resmi seçmek için **Ctrl+Shift+V** kullanın. Explorer’dan resim dosyası bırakabilir veya **Resim ekle** düğmesini kullanabilirsiniz. Notta resme çift tıklayınca görüntüleyici açılır.

Editörde **Ctrl+F** ile bulabilir, **Ctrl+H** ile değiştirebilir ve **Ctrl+Shift+F** ile geçerli JSON veya XML’i biçimlendirebilirsiniz. Tema ve yazı tipi **Ayarlar → Genel** bölümündedir.

### Sık kullanılan kısayollar

| Kısayol | İşlev |
| --- | --- |
| Ctrl+N | Yeni not |
| Ctrl+K | Not, grup ve komut paleti |
| Ctrl+S | Bekleyen düzenlemeleri hemen kaydet |
| Ctrl+Alt+N | Windows genelinde yeni not |
| Ctrl+Alt+V | Pano içeriğinden yeni not |
| Ctrl+Alt+K | Windows genelinde hızlı arama |
| Ctrl+Alt+Space | Kontrol merkezini göster veya gizle |

Tam liste ve Windows genelindeki tuş atamaları **Ayarlar → Kısayollar** bölümündedir.

## Yedekleme, taşıma ve şifreleme

- **Yedek:** **Ayarlar → Yedek ve aktarım** bölümünden yedek alın veya mevcut bir yedeği geri yükleyin. Resimler de veritabanıyla birlikte saklanır. Aynı bölümden metin notlarını içe aktarabilirsiniz.
- **Taşıma:** Sistem tepsisinden **Çıkış**’ı seçtikten sonra EXE’yi ve yanındaki **Veri** klasörünü beraber kopyalayın. Yeni konumda EXE’yi açın. Notlar, görseller ve ayarlar `Veri\notes.db` içinde tutulur.
- **Şifreleme:** **Ayarlar → Güvenlik** bölümünden yerel şifrelemeyi açabilirsiniz. Ana parolayı ve gösterilen 24 kelimelik kurtarma anahtarını uygulama klasöründen ayrı, güvenli bir yerde saklayın.
- **Senkron:** İki portable kopyada **Ayarlar → Klasör senkronu** bölümünden aynı paylaşılan klasörü ve aynı senkron parolasını kullanın. Google Drive, OneDrive ve iCloud için canlı hesap kabul testleri bekliyor. Windows Hello ile gerçek PIN testi de bekliyor.

## Doğrulayıcı ve Google Authenticator aktarımı

Doğrulayıcı’yı kullanmak için önce **Ayarlar → Güvenlik** üzerinden yerel şifrelemeyi açın.

1. Telefonda Google Authenticator’da **Hesapları aktar → Hesapları dışa aktar** bölümünü açıp taşımak istediğiniz hesapları seçin.
2. Windows’ta kenar çubuğundan **Doğrulayıcı**’yı açın. Aktarım QR görüntüsünü panoya kopyalayın ve **Panodan QR veya bağlantı ekle** düğmesini kullanın. Dosya olarak elinizdeyse pencereye bırakabilirsiniz. Birden çok QR üretilirse hepsini aynı pencerede sırayla okutun.
3. Hesap satırındaki koda tıklayıp kopyalayın. **Ctrl+K** paletinde `kod: hesap-adı` ile de bulabilirsiniz. TOTP kodunu telefondaki kodla aynı zaman diliminde karşılaştırın.

Google Authenticator aktarım biçiminin 2. sürümü **v2.1.1** ve sonrasında desteklenir. Doğrulayıcı ayrıca tek hesap için `otpauth://` bağlantısını ve elle TOTP/HOTP girişini destekler. Aktarım QR’ı hesapların gizli anahtarlarını içerir. Gerçek hesaplara ait QR’ları paylaşmayın.

## Güncelleme ve sorun giderme

- **Güncelleme:** Sol altta **↑ v… hazır** görünürse bağlantıya, ardından **Güncelle**’ye tıklayın. Elle kontrol için **Ayarlar → Güncellemeler → Şimdi kontrol et** yolunu kullanın. Uygulama EXE’nin SHA-256 özetini kontrol eder, veritabanını yedekler ve yeniden açılır.
- **Eski kopya:** Güncellemeler sekmesi bulunmuyorsa tepsiden çıkın, portable klasörü yedekleyin ve yeni EXE’yi mevcut EXE’nin yerine koyun. **Veri** klasörünü koruyun.
- **Notlar görünmüyor:** Açtığınız EXE’nin yanında doğru **Veri\notes.db** dosyasının bulunduğunu kontrol edin. Masaüstü kısayolu eski bir kopyaya gidiyor olabilir. Sürüm bilgisi sol alttadır.
- **QR okunmuyor:** Sürümü kontrol edin. QR’ın tamamını, kenarları kesilmeden ve parlama olmadan yeniden alın. Birden çok parça varsa diğer parçaları da okutun.
- **Destek:** Sorunu bildirirken Windows sürümünü, sol alttaki Karalama Defteri sürümünü ve tekrar adımlarını ekleyin. Not içeriği, parola, kurtarma kelimeleri ve gerçek hesap QR’larını paylaşmayın.

Geliştiren **Tekin Teknoloji** · [tekinteknoloji.com](https://tekinteknoloji.com)
