using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim;

/// <summary>
/// Yönetim paneli girişi (/yonetim/giris). Yalnızca panel yetkisi olan personel (StaffUsers, satış dışında en az bir
/// yetki) girebilir; izleyici hesapları burada geçersizdir, yalnızca satış yapan personel /giris sayfasını kullanır.
/// Başarılı girişte rollerden gelen yetkiler çereze yazılır. Kullanıcıda iki adımlı doğrulama açıksa önce e-postasına
/// kod gönderilir, oturum /dogrulama sayfasında kod girilince açılır (TwoFactorLogin).
/// </summary>
[EnableRateLimiting(AuthenticationSetup.LoginRateLimit)]
public class GirisModel(AppDbContext db, PasswordService passwords, TwoFactorLogin twoFactor) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>Gösterilecek hatanın dil anahtarı (ör. "auth.invalidLogin"); hata yoksa null.</summary>
    public string? ErrorKey { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Input.Email) || string.IsNullOrEmpty(Input.Password))
        {
            ErrorKey = "auth.invalidLogin";
            return Page();
        }

        var email = PasswordService.NormalizeEmail(Input.Email);
        var user = await db.StaffUsers.WithPermissions()
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken);

        // Kullanıcı yoksa da şifre doğrulaması yapılır (süre farkından e-postanın kayıtlı olup olmadığı anlaşılmasın).
        if (!passwords.Verify(user?.PasswordHash, Input.Password) || user is null)
        {
            ErrorKey = "auth.invalidLogin";
            return Page();
        }

        // Yalnızca satış yetkisi olan personel panele giremez (şifre doğru olduğu için nedenini söylemek sorun değil).
        if (!StaffAccounts.CanUsePanel(StaffAccounts.PermissionsOf(user)))
        {
            ErrorKey = "auth.noPanelAccess";
            return Page();
        }

        // İki adımlı doğrulama: kod e-postaya gider, oturum kod girilince açılır.
        if (twoFactor.IsRequired(user))
        {
            if (await twoFactor.BeginAsync(HttpContext, user, AuthSchemes.Staff, Input.RememberMe, ReturnUrl, cancellationToken))
                return Redirect(TwoFactorLogin.PagePath);
            ErrorKey = "auth.codeSendFailed";
            return Page();
        }

        await twoFactor.SignInAsync(HttpContext, user, AuthSchemes.Staff, Input.RememberMe, cancellationToken);
        return LocalRedirect(TwoFactorLogin.HomeOf(AuthSchemes.Staff, ReturnUrl));
    }
}
