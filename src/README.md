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
Docker'daki MySQL bilgisayara **3307** portundan açılır; böylece bilgisayarda zaten kurulu bir MySQL (3306) ile çakışmaz.
MySQL Workbench gibi bir araçla bağlanırken de `localhost:3307` kullanın.

### İlk giriş

Geliştirme ortamında ve Docker kurulumunda açılışta bir yönetici hesabı oluşturulur:
`admin@konferans.local` / `Yonetici.2026` (adres: http://localhost:8080/yonetim/giris).
Bu hesap yalnızca veritabanında hiç yönetim kullanıcısı yokken açılır; ayarlardaki `Admin:Email` ve
`Admin:Password` değerlerinden gelir. Canlı sunucuda bu değerleri değiştirin.

### Komut satırından

```
dotnet run --project ConferenceRoom.Web
dotnet test
```

## Sık yapılan işler

**Yeni metin eklemek:** `ConferenceRoom.Localization/Resources/tr.json` ve `en.json` dosyalarına aynı anahtarı ekleyin.
Sayfada `@T["bolum.anahtar"]` ile kullanın. Bir dilde unutulan anahtarı testler yakalar.

**Yeni dil eklemek:** `Resources` klasörüne `de.json` gibi bir dosya ekleyin (`_language.name` alanıyla). Dil seçicide kendiliğinden görünür.

**Yeni yetki eklemek:** `ConferenceRoom.Domain/Accounts/Permissions.cs` içine sabit ve `All` listesine ekleyin,
dil dosyalarına `permission.<kod>.name` / `.description` yazın. Sayfayı yetkiye bağlamak için:
`[Authorize(Policy = "Yetki:<kod>")]` (ya da `AuthPolicies.Permission(Permissions.X)`), bir klasörün tamamı için
`Program.cs` içinde `AuthorizeFolder`. Bölüm kullanıma açılınca panel kartına (`Pages/Yonetim/Index.cshtml`) ve
bölüm menüsüne (`Pages/Shared/_AdminLayout.cshtml`) adresini ekleyin.

**Veritabanında değişiklik:** varlığı `Domain` içinde değiştirin, eşlemesini `Infrastructure/Persistence/Configurations` içinde yapın, sonra:

```
dotnet tool restore
dotnet ef migrations add DegisiklikAdi -p ConferenceRoom.Infrastructure -s ConferenceRoom.Infrastructure -o Persistence/Migrations
```

## Kroki editörü

Yönetim panelinde Salonlar → Krokiyi aç. Dosyalar: `Pages/Yonetim/Salonlar/Kroki.cshtml` (sayfa ve kaydetme),
`wwwroot/js/kroki-editor.js` (editör), `wwwroot/css/kroki-editor.css`, `wwwroot/lib/konva` (çizim kütüphanesi, internet gerektirmez).
Metinler dil dosyalarının `planEditor` bölümündedir.

- **Çizim:** tek koltuk (S), koltuk sırası (R), koltuk bloğu (B), sahne (P), bölge (A), duvar (W), kapı/acil çıkış (D), sütun (C), yazı (T).
- **Kavisli sıra:** seçenek çubuğundaki "Kavis" değeri sırayı sahneye dönük bir yaya çevirir.
- **Seçim:** bir koltuğa tıklamak bloğun tamamını seçer; Alt+tık tek koltuk, Shift ekle/çıkar, boş alanda sürükleme alan seçimi.
- **Düzenleme:** taşıma, döndürme, boyutlandırma, hizalama, eşit dağıtma, yansıtma, gruplama (Ctrl+G), çoğaltma (Ctrl+D), geri al/yinele.
- **Numaralama:** sıralı, tek ya da çift numara; soldan sağa ya da sağdan sola; sıra harflerini yeniden verme.
- **Koltuk türleri:** normal, VIP/protokol, engelli yeri, ekstra sandalye. Kapasite özeti bunları ayrı sayar.
- **Katlar:** Parter, Balkon gibi katlar sekme olarak eklenir; bloklar kata bağlıdır.
- **Kaydetme:** Kaydet ya da Ctrl+S yeni sürüm oluşturur; değişiklik yoksa sürüm eklenmez. Editör açıldıktan sonra
  başkası yeni sürüm kaydettiyse kayıt reddedilir (üzerine yazılmasın diye). "{ }" penceresinde JSON kopyalama,
  içe aktarma ve örnek salon vardır.

Kroki verisi her sürümde tek bir JSON olarak saklanır (`HallPlanVersions.PlanJson`); yapısı ve sunucudaki denetimi
`ConferenceRoom.Domain/Halls/HallPlan.cs` içinde anlatılır. Ölçüler santimetre, `x,y` nesnenin merkezidir.

## Adresler

| Adres | İçerik |
| --- | --- |
| `/` | Ana sayfa (sistem durumu) |
| `/giris`, `/kayit` | Bilet alan izleyicinin girişi ve kaydı |
| `/yonetim/giris` | Yönetim paneli girişi (ayrı sayfa, ayrı çerez) |
| `/yonetim` | Yönetim paneli: rolün yetkili olduğu bölümler |
| `/yonetim/salonlar` | Salonlar; `/yonetim/salonlar/{id}/kroki` kroki editörü, `?surum=N` eski sürüm ("Salonlar ve kroki" yetkisi) |
| `/yonetim/kullanicilar`, `/yonetim/roller` | Yönetim kullanıcıları ve roller ("Kullanıcılar ve roller" yetkisi) |
| `/health` | Sağlık kontrolü (veritabanı dahil) |
| `/api/languages` | Kullanılabilir diller |
| `/api/languages/{dil}` | Bir dilin tüm metinleri (mobil uygulama için) |
| `/language/{dil}` | Dil seçimini değiştirir |
