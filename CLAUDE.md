# ConferenceRoom: proje hafızası

Bu dosya, projede çalışan Claude oturumlarının (ve geliştiricilerin) projeyi baştan öğrenmeden
devam edebilmesi için tutulur. Claude oturumlar arasında hafıza tutmaz; kalıcı bilgi buradadır.
Yeni bir karar alındığında ya da bir kural değiştiğinde bu dosya aynı PR içinde güncellenir.

## Proje

Konferans salonu yönetim sistemi. Açık kaynak (MIT), ücretsiz; yalnızca bağış kabul edilecek.
Proje sahibi ve mimar: Sevan (GitHub: `sevanakal`). Kodun tamamını Claude yazar, Sevan inceler ve birleştirir.
Arayüz dili Türkçe; kullanıcı ile yazışma Türkçe.

## Alınmış kararlar

| Konu | Karar |
| --- | --- |
| Altyapı | .NET 10 LTS, ASP.NET Core (Razor Pages + Web API). Blazor kullanılmıyor: ön yüz düz HTML/CSS kalsın, Sevan tasarımı doğrudan uygulayabilsin. Bootstrap/jQuery yok; stil `wwwroot/css/site.css` içindeki CSS değişkenleriyle. Visual Studio 2026 gerekir (.NET 10). |
| Veritabanı | MySQL 8.4, EF Core 10. Sağlayıcı: Oracle'ın resmi `MySql.EntityFrameworkCore` paketi, çünkü Pomelo henüz EF Core 10'u desteklemiyor (son sürümü 9.0.0). Lisansı GPL-2.0 + Universal FOSS Exception; açık kaynak MIT projede kullanılabilir. Pomelo EF Core 10 sürümü çıkarsa geçiş yalnızca `Infrastructure/DependencyInjection.cs` ve migration'ları etkiler. Türkçe sıralama için sunucu karşılaştırma kuralı `utf8mb4_tr_0900_ai_ci`. Kimlikler `int` otomatik artan. |
| Mobil | Aynı Web API'yi kullanacak. Önce PWA, gerekirse .NET MAUI. |
| Barındırma | Linux + Docker. Sevan'ın bilgisayarı Windows; Docker Desktop ile aynı şekilde çalışır. Visual Studio ile açılabilir olmalı. |
| Kurum | Başta tek kurum, ama her tabloda baştan kurum alanı (`OrganizationId`) bulunur; ileride çok kuruma geçilebilir. |
| Roller | Sabit kodlanmaz. Rol ve yetkiler veritabanında; yeni rol kod değiştirmeden eklenebilmeli. Başlangıç rolleri: Yönetici, Salon sorumlusu, Rezervasyon yapan kullanıcı. |
| Rezervasyon | Rezervasyonun birimi **etkinlik**tir; her etkinlik bir salonda yapılır. Onaylı mı anında mı kuralı etkinlik türünde varsayılan olarak tanımlanır, tek tek etkinlikte değiştirilebilir. |
| Dil | Ayrı `ConferenceRoom.Localization` kütüphanesi. Tüm buton, metin ve uyarılar `Resources/tr.json`, `en.json` dosyalarından gelir; arayüzde sabit metin yok. Standart `IStringLocalizer` arayüzüyle kullanılır (`@T["home.title"]`). Mobil aynı metinleri `/api/languages/{dil}` adresinden alır. Arayüz her zaman Türkçe açılır; tarayıcı dili dikkate alınmaz, kullanıcı dil seçiciden değiştirir. |
| Lisans | MIT (depoda `LICENSE`). |
| Tasarım | Grafik tasarım Sevan'da. Kod tarafı renkleri ve yazı tiplerini CSS değişkenlerinden okur ki tasarım kolayca değiştirilsin. |
| Salon krokisi | Projenin en önemli parçası. Photoshop tarzı 2B editör: koltuk sıraları, kavisli sıralar, bloklar, katlar (balkon), hizalama, gruplama, numaralama, ekstra sandalye. Kroki her değiştiğinde yeni sürüm olarak saklanır; etkinlik o anki sürüme bağlanır, böylece sonradan eklenen sandalye geçmiş etkinlikleri bozmaz. |
| Bilet satışı | İzleyici krokiden koltuk seçer. Fiyat koltuk kategorisinden gelir (blok/kat bazında); protokol koltukları satışa kapalı. Seçilen koltuk 10 dakika ayrılır; ayırma ileride sunucuda tutulacak. Gerçek ödeme henüz yok; ücretsiz ve bağışlı etkinlik de desteklenecek. |

## Depo yapısı

```
salon-kroki-editoru/   Kroki editörünün bağımsız prototipi (tek HTML dosyası). Sevan'ın onayladığı hâli.
                       Ana projeye taşınana kadar referans olarak burada durur. Bkz. salon-kroki-editoru/README.md
koltuk-secimi/         İzleyicinin krokiden koltuk seçip bilet aldığı ekranın prototipi (tek HTML dosyası).
                       Editörün kroki verisini okur; fiyat ve satılmış koltuklar örnek. Bkz. koltuk-secimi/README.md
src/                   ASP.NET Core çözümü (ConferenceRoom.slnx). Bkz. src/README.md
  ConferenceRoom.Domain          varlıklar; hiçbir projeye bağımlı değil
  ConferenceRoom.Infrastructure  EF Core, MySQL, migration'lar (Persistence/)
  ConferenceRoom.Localization    dil kütüphanesi (Resources/*.json)
  ConferenceRoom.Web             Razor Pages + Web API (Api/ klasörü), Program.cs
  tests/ConferenceRoom.Tests     xUnit testleri
  Directory.Build.props          ortak derleme ayarları (uyarılar hata sayılır)
  Directory.Packages.props       tüm NuGet sürümleri tek yerde
  Dockerfile, docker-compose.yml web + MySQL
.github/workflows/build.yml    her PR'da biçim denetimi, derleme, test (MySQL dahil)
```

## Komutlar (src klasöründe)

```
dotnet build
dotnet test                                    # MySQL testleri için: CONFERENCEROOM_TEST_MYSQL="Server=...;User=...;Password=..."
dotnet format                                  # CI biçim denetiminden geçmek için
dotnet tool restore
dotnet ef migrations add Ad -p ConferenceRoom.Infrastructure -s ConferenceRoom.Infrastructure -o Persistence/Migrations
docker compose up --build                      # http://localhost:8080
```

## Yeni bir varlık eklerken

1. `Domain/<Konu>/<Varlik>.cs`: `Entity`'den türet; kuruma aitse `IOrganizationScoped` uygula.
2. `Infrastructure/Persistence/Configurations/<Varlik>Configuration.cs`: tablo adı, uzunluklar, indeksler.
3. `AppDbContext` içine `DbSet` ekle, migration üret, migration'ı gerçek MySQL'de testle doğrula.
4. Arayüz metinlerini `tr.json` ve `en.json`'a birlikte ekle.

## Çalışma kuralları

- Her iş ayrı bir dalda yapılır ve PR olarak açılır; `main`'e doğrudan push edilmez. Sevan inceleyip birleştirir.
- Kod yorumları Türkçe. Her dosyanın başında ne işe yaradığı, her bölümün başında ne yaptığı yazılır.
  Amaç: projeye aylar sonra dönen biri (Claude dahil) dosyayı baştan okumadan yolunu bulabilsin.
- Karmaşıklığı büyütmeden ilerle: bir dosya çok büyürse bölünür, tekrar eden kod ortak yere alınır,
  kullanılmayan kod silinir. "Sonra temizleriz" denmez.
- Kullanıcıya görünen her metin dil dosyasından gelir (prototipte `I18N` nesnesi).
- Ölçüler kroki verisinde santimetre olarak tutulur.
- Dil anahtarları `bolum.anahtar` biçiminde (ör. `home.title`); bölüm sayfa ya da özellik adıdır.
- Test adları Türkçe ve cümle gibi yazılır (ör. `Ana_sayfa_varsayilan_olarak_turkce_acilir`).
