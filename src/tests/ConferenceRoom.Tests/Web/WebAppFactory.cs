using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ConferenceRoom.Tests.Web;

/// <summary>
/// Uygulamayı bellekte başlatır. Veritabanı bağlantısı erişilemeyen bir adrese yönlendirilir:
/// bu testler veritabanı olmadan da sayfaların ve API'nin çalıştığını doğrular. Şifreleme anahtarları normalde
/// veritabanında durduğu için burada bellekteki geçici anahtarlar kullanılır (formların sahtecilik anahtarı üretilebilsin).
/// </summary>
public sealed class WebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Server=127.0.0.1;Port=1;Database=yok;User=yok;Password=yok;Connect Timeout=1");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.ConfigureTestServices(services => services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()));
    }
}
