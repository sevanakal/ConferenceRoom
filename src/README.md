# ConferenceRoom: uygulama (src)

ASP.NET Core (.NET 10) web uygulaması ve MySQL veritabanı.

## Projeler

| Proje | Görevi |
| --- | --- |
| `ConferenceRoom.Domain` | Varlıklar (kurum, salon, etkinlik...). Veritabanını ya da web'i bilmez. |
| `ConferenceRoom.Infrastructure` | EF Core veritabanı bağlamı, tablo eşlemeleri, migration'lar, MySQL bağlantısı. |
| `ConferenceRoom.Localization` | Dil kütüphanesi. Tüm metinler `Resources/tr.json`, `Resources/en.json` dosyalarında. |
| `ConferenceRoom.Web` | Razor Pages arayüzü (HTML/CSS) ve Web API. |
| `tests/ConferenceRoom.Tests` | Testler. |

Bağımlılık yönü: `Web → Infrastructure → Domain`, `Web → Localization`. Domain hiçbir projeye bağlı değildir.

## Çalıştırma

### Docker ile (en kolay)

Docker Desktop kurulu olmalı. `src` klasöründe:

```
docker compose up --build
```

Tarayıcıda http://localhost:8080 adresini açın. MySQL de birlikte başlar, tablolar otomatik oluşur.

### Visual Studio ile

1. Visual Studio 2026 (.NET 10 desteği için gerekli) ile `src/ConferenceRoom.slnx` dosyasını açın.
2. MySQL çalışıyor olmalı. En kolayı yalnızca veritabanını Docker'da başlatmak: `docker compose up db`
3. Başlangıç projesi `ConferenceRoom.Web`, F5 ile çalıştırın. Geliştirme ortamında tablolar açılışta otomatik oluşur.

Bağlantı metni `ConferenceRoom.Web/appsettings.json` içindedir (kullanıcı `konferans`, şifre `gelistirme`).

### Komut satırından

```
dotnet run --project ConferenceRoom.Web
dotnet test
```

## Sık yapılan işler

**Yeni metin eklemek:** `ConferenceRoom.Localization/Resources/tr.json` ve `en.json` dosyalarına aynı anahtarı ekleyin.
Sayfada `@T["bolum.anahtar"]` ile kullanın. Bir dilde unutulan anahtarı testler yakalar.

**Yeni dil eklemek:** `Resources` klasörüne `de.json` gibi bir dosya ekleyin (`_language.name` alanıyla). Dil seçicide kendiliğinden görünür.

**Veritabanında değişiklik:** varlığı `Domain` içinde değiştirin, eşlemesini `Infrastructure/Persistence/Configurations` içinde yapın, sonra:

```
dotnet tool restore
dotnet ef migrations add DegisiklikAdi -p ConferenceRoom.Infrastructure -s ConferenceRoom.Infrastructure -o Persistence/Migrations
```

## Adresler

| Adres | İçerik |
| --- | --- |
| `/` | Ana sayfa (sistem durumu) |
| `/health` | Sağlık kontrolü (veritabanı dahil) |
| `/api/languages` | Kullanılabilir diller |
| `/api/languages/{dil}` | Bir dilin tüm metinleri (mobil uygulama için) |
| `/language/{dil}` | Dil seçimini değiştirir |
