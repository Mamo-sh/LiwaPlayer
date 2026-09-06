# LiwaPlayer

🇬🇧 [English version / İngilizce sürüm](README.md)

POS kasaları gibi düşük donanımlı Windows makineler için tasarlanmış hafif müzik çalar. YouTube'dan yalnızca ses akışı çalar — video kod çözümü yoktur, işlemci ve RAM kullanımı minimumdur.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blueviolet) ![WPF](https://img.shields.io/badge/UI-WPF-blue) ![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-lightgrey)

## Özellikler

- **Yerleşik YouTube araması** — tarayıcı açmadan video veya YouTube Music şarkısı arayıp listeye eklersin. Yalnızca ses akışı çalınır (video kod çözümü yok).
- **Kullanıcı tanımlı listeler** — liste yöneticisi penceresinden dilediğin kadar liste oluştur, aç, sil. Listeler JSON olarak saklanır.
- **Her yerden içe aktarma:**
  - **Spotify** listeleri ve albümleri (herkese açık bağlantılar) — her parçanın YouTube karşılığı süre eşleştirmesiyle otomatik bulunur.
  - **YouTube / YouTube Music** listeleri ve albümleri (paylaş bağlantısıyla).
  - **Yerel dosyalar** — MP3, M4A, FLAC, WAV, OGG ve daha fazlası; etiketler otomatik okunur.
- **YouTube hesap girişi** — istersen gömülü tarayıcı penceresinden giriş yaparsın. Premium hesapla çalma reklamsız olur ve kendi özel listelerin içe aktarılabilir. Oturum çerezleri Windows DPAPI ile şifrelenerek saklanır.
- **Doğum günü şarkısı otomatik tanıma** — başlığında doğum günüyle ilgili ifade geçen şarkılar (doğdun, doğacaksın gibi çekimler dahil) adında "doğum" geçen listeye otomatik yönlenir; kutlama şarkıları normal akışa karışmaz.
- **Arka planda çalma** — pencere kapatılınca uygulama sistem tepsisine iner, müzik kesilmez. Tepsi menüsünde oynat/duraklat, önceki/sonraki, güncelleme denetimi ve çıkış vardır.
- **Otomatik güncelleme** — açılışta GitHub Releases denetlenir; yeni sürüm tek tıkla kurulur.
- **POS dostu** — tek kopya çalışır, pencere konumu/ses/son liste hatırlanır, veriler `%LocalAppData%\LiwaPlayer` altında güvenle tutulur, kurulumdan Windows ile başlatma seçilebilir.

## Kurulum

[Releases](../../releases) sayfasından en son `LiwaPlayer-Setup-<sürüm>.exe` dosyasını indirip çalıştır. Kurulum:

- uygulamayı bağımsız (self-contained) kurar — makinede .NET kurulu olması gerekmez,
- istersen masaüstü kısayolu ve Windows ile başlatma kaydı oluşturur,
- hesap girişi için gereken Microsoft Edge WebView2 Runtime eksikse indirip kurar.

## Kaynaktan derleme

Gereksinimler: .NET 8 SDK, Windows 10+. Kurulum paketi için: [Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
# Geliştirme için çalıştır
dotnet run --project LiwaPlayer

# Kurulum paketini üret (self-contained yayın + setup)
.\Installer\build-setup.ps1 -Version 1.0.0
```

## Güncelleme yayınlama

1. Sürümü yükselterek paket üret: `.\Installer\build-setup.ps1 -Version 1.1.0`
2. GitHub'da `v1.1.0` etiketiyle release oluştur ve `Installer\Output\LiwaPlayer-Setup-1.1.0.exe` dosyasını ekle.
3. Kurulu uygulamalar açılışta (veya tepsi menüsü → *Güncellemeleri Denetle* ile) yeni sürümü görür, setup'ı indirir, sessizce kurar ve kendini yeniden başlatır.

## Teknolojiler

| Bileşen | Görev |
|---|---|
| WPF (.NET 8) | Arayüz, özel koyu tema |
| LibVLC / LibVLCSharp | Ses çalma (`--no-video`) |
| YoutubeExplode | YouTube arama, listeler, ses akışı çözümleme |
| YouTube Music InnerTube API | Yalnızca şarkı sonuçlu arama |
| Spotify embed uç noktası | Herkese açık liste/albüm okuma (API anahtarsız) |
| WebView2 | Google hesabı giriş penceresi |
| Inno Setup 6 | Kurulum ve sessiz otomatik güncelleme |

## Notlar

- Reklamsız çalma, giriş yapılan hesabın gerçekten YouTube Premium olmasına bağlıdır; uygulama kendisi reklam engellemez.
- Spotify içe aktarma yalnızca herkese açık içeriği okur ve liste başına ~100 parçayla sınırlıdır (embed uç noktası sınırı). YouTube'da karşılığı bulunamayan parçalar raporlanır; bunları elindeki dosyalardan elle ekleyebilirsin.
- Tüm kullanıcı verileri (listeler, ayarlar, oturum, günlük) `%LocalAppData%\LiwaPlayer\Data` altındadır ve uygulama güncellemelerinden etkilenmez.
