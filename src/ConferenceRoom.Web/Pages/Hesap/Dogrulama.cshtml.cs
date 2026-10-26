using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Hesap;

/// <summary>
/// İki adımlı doğrulamanın ikinci adımı (/dogrulama): personel e-postasına gelen 6 haneli kodu girer; doğruysa asıl oturum
/// (panel ya da satış) açılır. Sayfa yalnızca giriş sayfasının yazdığı kısa ömürlü "bekleyen giriş" çereziyle açılır
/// (TwoFactorLogin); çerez yoksa ya da süresi dolduysa girişe dönülür. Kod yeniden istenebilir.
/// </summary>
[EnableRateLimiting(AuthenticationSetup.LoginRateLimit)]
public class DogrulamaModel(AppDbContext db, AccountCodes codes, TwoFactorLogin twoFactor) : PageModel
{
    [BindProperty]
    public string? Code { get; set; }

    /// <summary>Kodun gönderildiği adresin gizlenmiş hâli (ör. "a•••@kurum.org").</summary>
    public string MaskedEmail { get; private set; } = "";

    /// <summary>Panel girişinden mi gelindi (görünüm ve "girişe dön" bağlantısı buna göre).</summary>
    public bool FromPanel { get; private set; }

    /// <summary>Gösterilecek hatanın ya da bilginin dil anahtarı.</summary>
    public string? ErrorKey { get; private set; }

    public string? InfoKey { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken) is null ? Redirect("/giris") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var state = await LoadAsync(cancellationToken);
        if (state is null)
            return Redirect("/giris");
        var (pending, user) = state.Value;

        switch (await codes.VerifyLoginCodeAsync(user.Id, Code, cancellationToken))
        {
            case AccountCodes.CodeResult.Ok:
                await HttpContext.SignOutAsync(AuthSchemes.TwoFactor);
                await twoFactor.SignInAsync(HttpContext, user, pending.Scheme, pending.Remember, cancellationToken);
                return LocalRedirect(TwoFactorLogin.HomeOf(pending.Scheme, pending.ReturnUrl));
            case AccountCodes.CodeResult.Wrong:
                ErrorKey = "twoFactor.wrong";
                break;
            default:
                ErrorKey = "twoFactor.expired";
                break;
        }
        Code = null;
        return Page();
    }

    /// <summary>Yeni kod gönderir (önceki kod geçersiz olur).</summary>
    public async Task<IActionResult> OnPostResendAsync(CancellationToken cancellationToken)
    {
        var state = await LoadAsync(cancellationToken);
        if (state is null)
            return Redirect("/giris");
        if (await twoFactor.SendCodeAsync(state.Value.User, cancellationToken))
            InfoKey = "twoFactor.resent";
        else
            ErrorKey = "auth.codeSendFailed";
        return Page();
    }

    // ---- Bekleyen giriş: çerez + kullanıcı hâlâ aktif ve o bölüme girebiliyor mu ----

    private async Task<(TwoFactorLogin.Pending Pending, Domain.Accounts.StaffUser User)?> LoadAsync(CancellationToken cancellationToken)
    {
        var pending = await TwoFactorLogin.ReadPendingAsync(HttpContext);
        if (pending is null)
            return null;
        var user = await db.StaffUsers.WithPermissions().FirstOrDefaultAsync(u => u.Id == pending.UserId && u.IsActive, cancellationToken);
        var permissions = user is null ? null : StaffAccounts.PermissionsOf(user);
        var allowed = permissions is not null && (pending.Scheme == AuthSchemes.Staff ? StaffAccounts.CanUsePanel(permissions) : StaffAccounts.CanSell(permissions));
        if (!allowed)
        {
            await HttpContext.SignOutAsync(AuthSchemes.TwoFactor);
            return null;
        }
        FromPanel = pending.Scheme == AuthSchemes.Staff;
        MaskedEmail = Mask(user!.Email);
        return (pending, user);
    }

    private static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : email[0] + new string('•', Math.Min(at - 1, 5)) + email[at..];
    }
}
