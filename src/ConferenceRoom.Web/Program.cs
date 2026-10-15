// ConferenceRoom.Web giriş noktası.
// Sıra: servisleri kaydet → uygulamayı kur → istek hattını (middleware) sırayla ekle → çalıştır.
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using ConferenceRoom.Infrastructure;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Localization;
using ConferenceRoom.Web.Api;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

// ---- Servisler ----
builder.Services.AddRazorPages();
// Türkçe karakterler (ş, ğ, ı...) HTML'de &#x15F; gibi kodlanmadan, olduğu gibi yazılsın.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddJsonLocalization();                       // tüm metinler ConferenceRoom.Localization/Resources/*.json
builder.Services.AddInfrastructure(builder.Configuration);    // MySQL + EF Core
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("veritabani");

var app = builder.Build();

// ---- Veritabanı ----
// Docker kurulumunda açılışta migration'lar otomatik uygulanır (docker-compose.yml: Database__MigrateOnStartup=true).
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    await DatabaseMigrator.MigrateAsync(app.Services);

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
app.UseAuthorization();

app.MapRazorPages();
app.MapHealthChecks("/health");
app.MapLanguageEndpoints();

app.Run();

// Testlerin (WebApplicationFactory) uygulamayı başlatabilmesi için görünür kılınır.
public partial class Program;
