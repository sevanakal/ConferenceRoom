using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Hesap;

/// <summary>
/// Genel giriş (/giris): bilet alan izleyici (Customers) ve satış personeli. Satış yetkisi ("sales.sell") olan personel
/// burada girince satış bölümüne (/satis) gider; bu oturumla yönetim paneli açılmaz (panel kendi sayfasından,
/// /yonetim/giris, ayrı çerezle girilir). Satış yetkisi olmayan personelin bilgileri burada geçersizdir.
/// Aynı e-posta hem personelde hem izleyicide varsa önce personel şifresi denenir. Personelde iki adımlı doğrulama açıksa
/// oturum /dogrulama sayfasında e-postaya gelen kod girilince açılır (TwoFactorLogin).
/// </summary>
[EnableRateLimiting(AuthenticationSetup.LoginRateLimit)]
public class GirisModel(AppDbContext db, PasswordService passwords, TwoFactorLogin twoFactor) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>Gösterilecek hatanın dil anahtarı; hata yoksa null.</summary>
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

        // ---- Satış personeli ----
        var staff = await db.StaffUsers.WithPermissions().FirstOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken);
        if (staff is not null && passwords.Verify(staff.PasswordHash, Input.Password) && StaffAccounts.CanSell(StaffAccounts.PermissionsOf(staff)))
        {
            if (twoFactor.IsRequired(staff))
            {
                if (await twoFactor.BeginAsync(HttpContext, staff, AuthSchemes.Sales, Input.RememberMe, ReturnUrl, cancellationToken))
                    return Redirect(TwoFactorLogin.PagePath);
                ErrorKey = "auth.codeSendFailed";
                return Page();
            }
            await twoFactor.SignInAsync(HttpContext, staff, AuthSchemes.Sales, Input.RememberMe, cancellationToken);
            return LocalRedirect(TwoFactorLogin.HomeOf(AuthSchemes.Sales, ReturnUrl));
        }

        // ---- İzleyici ----
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.OrganizationId == Organization.DefaultId && c.Email == email && c.IsActive, cancellationToken);

        if (!passwords.Verify(customer?.PasswordHash, Input.Password) || customer is null)
        {
            ErrorKey = "auth.invalidLogin";
            return Page();
        }

        customer.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await HttpContext.SignInAsync(AuthSchemes.Customer, Principals.ForCustomer(customer),
            new AuthenticationProperties { IsPersistent = Input.RememberMe });
        return LocalRedirect(Principals.SafeReturnUrl(ReturnUrl, "/"));
    }
}
