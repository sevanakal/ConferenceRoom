using System.Threading.RateLimiting;
using ConferenceRoom.Domain.Accounts;

namespace ConferenceRoom.Web.Auth;

/// <summary>
/// Kimlik doğrulama, yetkilendirme ve giriş denemesi sınırının kurulumu. Program.cs yalnızca
/// <see cref="AddAppAuthentication"/> çağırır; ayrıntılar burada toplanır.
/// </summary>
public static class AuthenticationSetup
{
    /// <summary>Giriş ve kayıt formlarına uygulanan deneme sınırı. Yalnızca form gönderimleri (POST) sayılır;
    /// IP başına dakikada "Auth:LoginAttemptsPerMinute" (varsayılan 10) deneme.</summary>
    public const string LoginRateLimit = "giris";

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<PasswordService>();

        // ---- Çerezler: iki ayrı şema ----
        // Genel sayfalarda (ana sayfa, bilet ekranı) varsayılan kimlik izleyicinin çerezidir.
        // Yönetim sayfaları kendi kuralında Staff şemasını ister; izleyici çerezi orada geçersizdir.
        services.AddAuthentication(AuthSchemes.Customer)
            .AddCookie(AuthSchemes.Customer, o =>
            {
                o.Cookie.Name = ".konferans.uye";
                o.LoginPath = "/giris";
                o.LogoutPath = "/cikis";
                o.AccessDeniedPath = "/giris";
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
                o.SlidingExpiration = true;
            })
            .AddCookie(AuthSchemes.Staff, o =>
            {
                o.Cookie.Name = ".konferans.yonetim";
                o.Cookie.Path = "/yonetim";               // tarayıcı bu çerezi yönetim dışındaki adreslere göndermez
                o.LoginPath = "/yonetim/giris";
                o.LogoutPath = "/yonetim/cikis";
                o.AccessDeniedPath = "/yonetim/yetkisiz";
                o.ExpireTimeSpan = TimeSpan.FromHours(10);  // bir iş günü
                o.SlidingExpiration = true;
            });

        // ---- Kurallar: panele giriş + her yetki için bir kural ----
        var authorization = services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Staff, p => p.AddAuthenticationSchemes(AuthSchemes.Staff).RequireAuthenticatedUser());
        foreach (var permission in Permissions.All)
        {
            authorization.AddPolicy(AuthPolicies.Permission(permission), p => p
                .AddAuthenticationSchemes(AuthSchemes.Staff)
                .RequireAuthenticatedUser()
                .RequireClaim(AppClaims.Permission, permission));
        }

        // ---- Kaba kuvvet denemelerine karşı: IP başına dakikada sınırlı giriş/kayıt denemesi ----
        var attemptsPerMinute = configuration.GetValue("Auth:LoginAttemptsPerMinute", 10);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimit, context => HttpMethods.IsPost(context.Request.Method)
                ? RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "bilinmiyor",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = attemptsPerMinute, Window = TimeSpan.FromMinutes(1) })
                : RateLimitPartition.GetNoLimiter("sayfa"));
        });

        return services;
    }
}
