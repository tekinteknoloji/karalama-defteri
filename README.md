# Karalama Defteri

**Windows için Türkçe, taşınabilir not defteri.** Müşteri ve proje notlarını firmalara göre ayırın, ekran görüntülerini notların içine ekleyin ve aradığınızı Ctrl+K ile bulun. Yazdıklarınız otomatik kaydedilir.

[Son sürümü indir](https://github.com/tekinteknoloji/karalama-defteri/releases/latest) · [Sürüm notları](https://github.com/tekinteknoloji/karalama-defteri/releases)

## v2.3.0 ve v2.3.1

- **v2.3.0** ile Özellikler, Pano ve Windows hatırlatmaları eklendi. [v2.3.0 sürüm notları](https://github.com/tekinteknoloji/karalama-defteri/releases/tag/v2.3.0)
- **v2.3.1** ile Ayarlar ve güncelleme penceresi yenilendi, metin seçimi ve Windows Hello akışı düzeltildi. [v2.3.1 sürüm notları](https://github.com/tekinteknoloji/karalama-defteri/releases/tag/v2.3.1)

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

## Özellikler, Pano ve hatırlatmalar

**Ayarlar → Özellikler** bölümünden Pano, Son tarih ve hatırlatma, Doğrulayıcı, Masaüstüne iğneleme, Etiketler, Kontrol listesi ve Bulut senkronunu yönetebilirsiniz. Bir modülü kapatınca ilgili komutlar gizlenir ve kayıtlı veriler korunur. **Özellik seçimlerimi diğer cihazlarla eşitle** ile tercihlerinizi bağlı kopyalara taşıyabilirsiniz.

### Pano

1. **Ayarlar → Özellikler → Pano (proje takibi)** seçeneğini açın. **Görünüm → Pano** yolunu kullanın veya **Ctrl+K** ile **Pano** komutunu bulun.
2. Kenar çubuğundan bir grup veya **Tümü** seçeneğini seçin. Başlangıç sütunları **Fikir**, **Yapılacak**, **Yapılıyor** ve **Bitti**. Sütun ekleyebilir, adını ve sırasını değiştirebilirsiniz.
3. Kartları sütunlar arasında veya aynı sütun içinde sürükleyin. Kart odaktayken **Ctrl+Shift+←/→** sütunu, **Ctrl+Shift+↑/↓** sırayı değiştirir.

### Son tarih ve hatırlatma

Pano açıkken **Ayarlar → Özellikler → Son tarih ve hatırlatma** seçeneğini açın. Kartın sağ tık menüsünden **Son tarih ve hatırlatma…** ile tarih ve saat belirleyin. Windows bildirimi için karttaki bildirim seçeneğini işaretleyin. Uygulama tepside çalışırken de bildirim gelir.

Kart kenarı son 24 saatte sarıya, süre dolunca kırmızıya döner. **Yaklaşanlar** seçeneği veya `tarih:hafta` filtresi bugün dahil yedi günü gösterir. Bugün için `tarih:bugün` yazın.

## Windows Hello

1. **Ayarlar → Güvenlik** üzerinden yerel şifrelemeyi açın. 24 kelimelik kurtarma anahtarını uygulama klasöründen ayrı saklayın.
2. Windows hesabında Hello kuruluysa **Windows Hello’yu etkinleştir…** seçeneğine tıklayın. Karalama Defteri ana parolanızı girip Windows istemini tamamlayın.
3. **Şimdi kilitle** ile deneyin. Açılış ekranında **Windows Hello ile aç** seçeneğini kullanın. PIN istenirse yalnızca Windows ekranına girin.

Hello kullanılamadığında veya portable klasörü başka bir bilgisayara taşıdığınızda ana parolayla açabilirsiniz. Yeni bilgisayarda Hello bağlantısını yeniden kurabilirsiniz.

## Senkron

- **Klasör:** İki portable kopyada **Ayarlar → Senkron → Klasör** bölümünden aynı paylaşılan klasörü seçin. Aynı senkron parolasını kullanın. İki cihaz aynı notu değiştirirse diğer düzenleme çakışma kopyasında saklanır.
- **Google Drive:** **Bulut senkronu** modülünü açık tutun. Google Cloud’dan indirdiğiniz masaüstü OAuth JSON dosyasını `Veri/google-oauth.json` konumuna koyun. **Ayarlar → Senkron → Google Drive ile eşitle…** ile Google izin ekranını tamamlayın. OAuth dosyasını yerelde saklayın.
- **OneDrive ve iCloud:** OneDrive düğmesi yapılandırılmış OAuth istemcisiyle etkinleşir. iCloud seçeneği bu sürümde pasiftir.
- **Otomatik eşitleme:** Bulut için **Değişiklik olunca**, **Saatlik** veya **Günde bir** seçilebilir. Yerel şifreleme açıksa bağlantı kurulduktan sonra ayrı senkron parolası sorulmaz. Şifreleme kapalıysa senkron parolası ilk bağlantıda istenir. Eşitleme durumunu sol alttaki göstergeden takip edin.

## Yedekleme, taşıma ve şifreleme

- **Yedek:** **Ayarlar → Yedek ve aktarım** bölümünden yedek alın veya mevcut bir yedeği geri yükleyin. Resimler de veritabanıyla birlikte saklanır. Aynı bölümden metin notlarını içe aktarabilirsiniz.
- **Taşıma:** Sistem tepsisinden **Çıkış**’ı seçtikten sonra EXE’yi ve yanındaki **Veri** klasörünü beraber kopyalayın. Yeni konumda EXE’yi açın. Notlar, görseller ve ayarlar `Veri\notes.db` içinde tutulur.
- **Şifreleme:** **Ayarlar → Güvenlik** bölümünden yerel şifrelemeyi açabilirsiniz. Ana parolayı ve gösterilen 24 kelimelik kurtarma anahtarını uygulama klasöründen ayrı, güvenli bir yerde saklayın.

## Doğrulayıcı ve Google Authenticator aktarımı

Önce **Ayarlar → Güvenlik** üzerinden yerel şifrelemeyi açın. Ardından **Ayarlar → Özellikler** bölümünden **Doğrulayıcı** modülünü etkinleştirin.

1. Telefonda Google Authenticator’da **Hesapları aktar → Hesapları dışa aktar** bölümünü açıp taşımak istediğiniz hesapları seçin.
2. Windows’ta kenar çubuğundan **Doğrulayıcı**’yı açın. Aktarım QR görüntüsünü panoya kopyalayın ve **QR veya bağlantı ekle** düğmesini kullanın. Dosya olarak elinizdeyse pencereye bırakabilirsiniz. Birden çok QR üretilirse hepsini aynı pencerede sırayla okutun.
3. Hesap satırındaki koda tıklayıp kopyalayın. **Ctrl+K** paletinde `kod: hesap-adı` ile de bulabilirsiniz. TOTP kodunu telefondaki kodla aynı zaman diliminde karşılaştırın.

Google Authenticator aktarım biçiminin 2. sürümü **v2.1.1** ve sonrasında desteklenir. Doğrulayıcı ayrıca tek hesap için `otpauth://` bağlantısını ve elle TOTP/HOTP girişini destekler. Aktarım QR’ı hesapların gizli anahtarlarını içerir. Gerçek hesaplara ait QR’ları paylaşmayın.

## Güncelleme ve sorun giderme

- **Güncelleme:** Sol altta **↑ v… hazır** görünürse bağlantıya, ardından **Güncelle**’ye tıklayın. Elle kontrol için **Ayarlar → Güncellemeler → Şimdi kontrol et** yolunu kullanın. Uygulama EXE’nin SHA-256 özetini kontrol eder, veritabanını yedekler ve yeniden açılır.
- **Eski kopya:** Güncellemeler sekmesi bulunmuyorsa tepsiden çıkın, portable klasörü yedekleyin ve yeni EXE’yi mevcut EXE’nin yerine koyun. **Veri** klasörünü koruyun.
- **Notlar görünmüyor:** Açtığınız EXE’nin yanında doğru **Veri\notes.db** dosyasının bulunduğunu kontrol edin. Masaüstü kısayolu eski bir kopyaya gidiyor olabilir. Sürüm bilgisi sol alttadır.
- **QR okunmuyor:** Sürümü kontrol edin. QR’ın tamamını, kenarları kesilmeden ve parlama olmadan yeniden alın. Birden çok parça varsa diğer parçaları da okutun.
- **Destek:** Sorunu bildirirken Windows sürümünü, sol alttaki Karalama Defteri sürümünü ve tekrar adımlarını ekleyin. Not içeriği, parola, kurtarma kelimeleri ve gerçek hesap QR’larını paylaşmayın.


## Derleme ve yayın

Windows’ta .NET 8 SDK ile proje kökünden çalıştırın.

```powershell
dotnet build
dotnet test
dotnet publish src/KaralamaDefteri -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/win-x64
```

## GitHub Releases için güncelleme paketi

Google Drive canlı denemesi için Google Cloud masaüstü OAuth dosyasını yerelde `secrets/google-oauth.json` olarak saklayın. `Publish-Release.ps1 -GoogleOAuthTest` istemci kimliğini deneme EXE’sine ekler. Deneme sürecini başlatırken istemci sırrını aynı dosyadan `KARALAMA_GOOGLE_DESKTOP_SECRET` ortam değişkenine yükleyin. İstemci sırrı derleme çıktısına eklenmez. [Gizlilik politikası](https://tekinteknoloji.github.io/karalama-defteri/PRIVACY.html)

`Publish-Release.ps1` EXE, `SHA256SUMS` ve `latest.json` üretir. `-AssetUrl` ile EXE'nin sürüme özel HTTPS indirme adresini, `-Notes` ile sürüm notlarını ve gerekirse `-MinVersion` ile güncellenebilecek en eski sürümü verin. Kararlı sürümün üç dosyasını GitHub'daki kararlı sürüme ekleyin. Beta için dosyaları sürüme özel ön sürüme ekleyin ve `beta` etiketli ön sürümün `latest.json` dosyasını güncelleyin. Manifestteki EXE adresi sürüme özel kalır.

İmzalama için isteğe bağlı `-SignerScript` parametresine EXE yolunu ilk argüman olarak alan PowerShell betiğini verin. Betik geçerli Authenticode imzasını doğruladıktan sonra SHA-256 üretir. `-SignerThumbprint` ile sonraki güncellemelerde beklenen imzalayan sertifika belirtilebilir.

## Destek ol

Karalama Defteri ücretsiz ve reklamsızdır. Destek gönüllüdür ve tüm kullanıcılar aynı özelliklere erişir. **Hakkında → Destek ol** bölümünden GitHub’da yıldız verebilir, hata veya öneri bildirebilir ve indirme bağlantısını paylaşabilirsiniz. Ödeme düğmeleri bağlantılar yapılandırıldığında görünür.
Geliştiren **Tekin Teknoloji** · [tekinteknoloji.com](https://tekinteknoloji.com)
