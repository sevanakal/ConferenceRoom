using System.Globalization;
using System.Security.Claims;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Email;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Web.Auth;

/// <summary>
/// İki adımlı doğrulama (personel; kullanıcı başına açılır, <see cref="StaffUser.TwoFactorEnabled"/>).
/// Akış: giriş sayfası şifreyi doğrular → <see cref="BeginAsync"/> e-postaya 6 haneli kod gönderir ve kısa ömürlü
/// "doğrulama bekliyor" çerezi yazar → /dogrulama sayfası kodu alır → doğruysa asıl oturum (panel ya da satış) açılır.
/// Bekleyen çerez yalnızca kimliği, hangi oturumun açılacağını, "beni hatırla" ve dönüş adresini taşır; kendisiyle hiçbir
/// sayfa açılmaz. Acil durumda (ör. SMTP bozuldu, yönetici giremiyor) "Auth:DisableTwoFactor=true" ayarı doğrulamayı kapatır.
/// </summary>
public sealed class TwoFactorLogin(AppDbContext db, AccountCodes codes, IEmailSender email, IStringLocalizer T, IConfiguration configuration)
{
    public const string PagePath = "/dogrulama";

    /// <summary>Bekleyen çerezin ömrü: kodun ömründen biraz uzun (kod yeniden istenebilsin).</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(15);

    private const string TargetClaim = "hedef", RememberClaim = "hatirla", ReturnUrlClaim = "donus";

    /// <summary>Doğrulama bekleyen giriş.</summary>
    /// <param name="Scheme">Açılacak oturum: <see cref="AuthSchemes.Staff"/> ya da <see cref="AuthSchemes.Sales"/>.</param>
    public sealed record Pending(int UserId, string Scheme, bool Remember, string? ReturnUrl);

    /// <summary>Bu kullanıcı girişte kod girmeli mi.</summary>
    public bool IsRequired(StaffUser user) => user.TwoFactorEnabled && !configuration.GetValue<bool>("Auth:DisableTwoFactor");

    // ---- Başlatma ve kod gönderme ----

    /// <summary>Kodu gönderir ve bekleyen çerezi yazar. Kod gönderilemezse false (giriş sayfası hata gösterir).</summary>
    public async Task<bool> BeginAsync(HttpContext http, StaffUser user, string scheme, bool remember, string? returnUrl, CancellationToken cancellationToken)
    {
        if (!await SendCodeAsync(user, cancellationToken))
            return false;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(TargetClaim, scheme),
            new(RememberClaim, remember ? "1" : "0"),
        };
        if (!string.IsNullOrEmpty(returnUrl))
            claims.Add(new Claim(ReturnUrlClaim, returnUrl));
        await http.SignInAsync(AuthSchemes.TwoFactor, new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.TwoFactor)),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow + PendingLifetime });
        return true;
    }

    /// <summary>Yeni kod üretip kullanıcının e-postasına gönderir (önceki kod geçersiz olur). Gönderilemezse false.</summary>
    public async Task<bool> SendCodeAsync(StaffUser user, CancellationToken cancellationToken)
    {
        var code = await codes.CreateLoginCodeAsync(user.Id, cancellationToken);
        var minutes = (int)AccountCodes.LoginCodeLifetime.TotalMinutes;
        try
        {
            await email.SendAsync(new EmailMessage(user.Email, T["email.codeSubject", code], T["email.codeBody", user.FullName, code, minutes]), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is EmailNotConfiguredException or EmailSendException)
        {
            return false;
        }
    }

    // ---- Bekleyen giriş ve oturumu açma ----

    /// <summary>Tarayıcıdaki bekleyen giriş; yoksa ya da süresi dolduysa null.</summary>
    public static async Task<Pending?> ReadPendingAsync(HttpContext http)
    {
        var result = await http.AuthenticateAsync(AuthSchemes.TwoFactor);
        var p = result.Succeeded ? result.Principal : null;
        if (p is null || !int.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out var id))
            return null;
        var scheme = p.FindFirstValue(TargetClaim);
        if (scheme is not (AuthSchemes.Staff or AuthSchemes.Sales))
            return null;
        return new Pending(id, scheme, p.FindFirstValue(RememberClaim) == "1", p.FindFirstValue(ReturnUrlClaim));
    }

    /// <summary>
    /// Personelin asıl oturumunu açar (şifre ya da şifre + kod doğrulandıktan sonra; giriş sayfaları ve /dogrulama ortak
    /// kullanır). Son giriş zamanını yazar. Roles → Role → Permissions yüklenmiş olmalı.
    /// </summary>
    public async Task SignInAsync(HttpContext http, StaffUser user, string scheme, bool remember, CancellationToken cancellationToken)
    {
        user.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await http.SignInAsync(scheme, Principals.ForStaff(user, scheme), new AuthenticationProperties { IsPersistent = remember });
    }

    /// <summary>Girişten sonra gidilecek adres: dönüş adresi bölümün içindeyse o, değilse bölümün ana sayfası.</summary>
    public static string HomeOf(string scheme, string? returnUrl) => scheme == AuthSchemes.Staff
        ? Principals.SafeReturnUrl(returnUrl, "/yonetim", area: "/yonetim")
        : Principals.SafeReturnUrl(returnUrl, "/satis", area: "/satis");
}
