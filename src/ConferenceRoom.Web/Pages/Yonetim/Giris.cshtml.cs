using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim;

/// <summary>
/// Yönetim paneli girişi (/yonetim/giris). Yalnızca yönetim kullanıcıları (StaffUsers) girebilir;
/// izleyici hesapları burada geçersizdir. Başarılı girişte rollerden gelen yetkiler çereze yazılır.
/// </summary>
[EnableRateLimiting(AuthenticationSetup.LoginRateLimit)]
public class GirisModel(AppDbContext db, PasswordService passwords) : PageModel
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

        user.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await HttpContext.SignInAsync(AuthSchemes.Staff, Principals.ForStaff(user),
            new AuthenticationProperties { IsPersistent = Input.RememberMe });
        return LocalRedirect(Principals.SafeReturnUrl(ReturnUrl, "/yonetim", area: "/yonetim"));
    }
}
