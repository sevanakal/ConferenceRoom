// ConferenceRoom.Web giriş noktası.
// Sıra: servisleri kaydet → uygulamayı kur → istek hattını (middleware) sırayla ekle → çalıştır.
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using ConferenceRoom.Infrastructure;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Localization;
using ConferenceRoom.Web.Api;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

// ---- Servisler ----
// /yonetim altındaki her sayfa yönetim girişi ister; giriş sayfasının kendisi hariç.
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Yonetim", AuthPolicies.Staff);
    o.Conventions.AllowAnonymousToPage("/Yonetim/Giris");
});
// Türkçe karakterler (ş, ğ, ı...) HTML'de &#x15F; gibi kodlanmadan, olduğu gibi yazılsın.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddJsonLocalization();                       // tüm metinler ConferenceRoom.Localization/Resources/*.json
builder.Services.AddInfrastructure(builder.Configuration);    // MySQL + EF Core
builder.Services.AddAppAuthentication(builder.Configuration); // iki ayrı giriş (yönetim / izleyici), yetkiler (bkz. Auth/)
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
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapRazorPages();
app.MapHealthChecks("/health");
app.MapLanguageEndpoints();

app.Run();

// Testlerin (WebApplicationFactory) uygulamayı başlatabilmesi için görünür kılınır.
public partial class Program;
