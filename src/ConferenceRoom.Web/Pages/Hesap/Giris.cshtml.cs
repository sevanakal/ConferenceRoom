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
/// Bilet alan izleyicinin girişi (/giris). Yalnızca izleyici hesapları (Customers) girebilir;
/// yönetim kullanıcıları kendi sayfalarından (/yonetim/giris) girer. Otomatik yönlendirme yoktur.
/// </summary>
[EnableRateLimiting(AuthenticationSetup.LoginRateLimit)]
public class GirisModel(AppDbContext db, PasswordService passwords) : PageModel
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
