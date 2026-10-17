// ConferenceRoom.Web giriş noktası.
// Sıra: servisleri kaydet → uygulamayı kur → istek hattını (middleware) sırayla ekle → çalıştır.
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Infrastructure;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Localization;
using ConferenceRoom.Web.Api;
using ConferenceRoom.Web.Auth;
using ConferenceRoom.Web.Events;
using ConferenceRoom.Web.Sales;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

// ---- Servisler ----
// /yonetim altındaki her sayfa yönetim girişi ister; giriş sayfasının kendisi hariç.
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Yonetim", AuthPolicies.Staff);
    o.Conventions.AllowAnonymousToPage("/Yonetim/Giris");
    // Bölüm klasörleri ayrıca kendi yetkisini ister (yetki kodları: Domain/Accounts/Permissions.cs).
    o.Conventions.AuthorizeFolder("/Yonetim/Kullanicilar", AuthPolicies.Permission(Permissions.UsersManage));
    o.Conventions.AuthorizeFolder("/Yonetim/Roller", AuthPolicies.Permission(Permissions.UsersManage));
    o.Conventions.AuthorizeFolder("/Yonetim/Salonlar", AuthPolicies.Permission(Permissions.HallsManage));
    o.Conventions.AuthorizeFolder("/Yonetim/Etkinlikler", AuthPolicies.Permission(Permissions.EventsManage));
    o.Conventions.AuthorizeFolder("/Yonetim/Fiyatlandirma", AuthPolicies.Permission(Permissions.PricingManage));
    o.Conventions.AuthorizeFolder("/Yonetim/SatisYetkileri", AuthPolicies.Permission(Permissions.UsersManage));
    // Satış bölümü: satış personeli /giris sayfasından girer (ayrı çerez; panel bu oturumla açılmaz).
    o.Conventions.AuthorizeFolder("/Satis", AuthPolicies.Sales);
    // Ana sayfa etkinlik panosudur; /pano adresi lobideki ekranlar için kalır.
    o.Conventions.AddPageRoute("/Pano", "");
});
// Türkçe karakterler (ş, ğ, ı...) HTML'de &#x15F; gibi kodlanmadan, olduğu gibi yazılsın.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddJsonLocalization();                       // tüm metinler ConferenceRoom.Localization/Resources/*.json
builder.Services.AddInfrastructure(builder.Configuration);    // MySQL + EF Core
builder.Services.AddAppAuthentication(builder.Configuration); // ayrı oturumlar (yönetim / satış / izleyici), yetkiler (bkz. Auth/)
builder.Services.AddSingleton(TimeProvider.System);            // "şu an" tek yerden gelsin (testlerde değiştirilebilir)
builder.Services.AddSingleton<PosterStore>();                  // etkinlik afişleri (diskte; Docker'da ayrı birim)
builder.Services.AddSignalR();                                 // satış ekranlarının canlı bağlantısı (Sales/SalesHub.cs)
builder.Services.AddSingleton<SeatHolds>();                    // 3 dakikalık koltuk ayırmaları (bellekte)
builder.Services.AddHostedService<SeatHoldExpiry>();           // süresi dolan ayırmaları kaldırıp ekranlara bildirir
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("veritabani");

var app = builder.Build();

// ---- Veritabanı ----
// Docker kurulumunda açılışta migration'lar otomatik uygulanır (docker-compose.yml: Database__MigrateOnStartup=true).
// Ayarlarda Admin:Email/Admin:Password varsa ve hiç yönetim kullanıcısı yoksa ilk yönetici hesabı açılır.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await DatabaseMigrator.MigrateAsync(app.Services);
    await AdminBootstrapper.EnsureAsync(app.Services, app.Configuration);
}

// ---- İstek hattı ----
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Dil seçimi: önce adres satırındaki ?culture=en, sonra dil seçicinin yazdığı çerez; ikisi de yoksa Türkçe.
// Tarayıcının dili bilerek dikkate alınmaz: arayüz Türkçe açılır, kullanıcı isterse dili kendisi değiştirir.
var catalog = app.Services.GetRequiredService<LanguageCatalog>();
var cultures = catalog.Languages.Select(l => new CultureInfo(l.Culture)).ToList();
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(LanguageCatalog.DefaultCulture),
    SupportedCultures = cultures,
    SupportedUICultures = cultures,
    FallBackToParentCultures = true,
    FallBackToParentUICultures = true,
    RequestCultureProviders = [new QueryStringRequestCultureProvider(), new CookieRequestCultureProvider()],
});

app.UseStaticFiles();
// Yüklenen afişler wwwroot dışında, ayrı bir klasörde durur (bkz. Events/PosterStore.cs).
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(app.Services.GetRequiredService<PosterStore>().Root),
    RequestPath = PosterStore.RequestPath,
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapRazorPages();
app.MapHub<SalesHub>(SalesHub.Path);
app.MapHealthChecks("/health");
app.MapLanguageEndpoints();

app.Run();

// Testlerin (WebApplicationFactory) uygulamayı başlatabilmesi için görünür kılınır.
public partial class Program;
