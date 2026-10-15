using System.Text.RegularExpressions;
using ConferenceRoom.Domain.Accounts;
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
/// İzleyici kaydı (/kayit). Hesap açılınca kullanıcı doğrudan giriş yapmış olur.
/// Hatalar alan bazında toplanır; görünüm her alanın altına kendi hatasını yazar.
/// </summary>
[EnableRateLimiting(AuthenticationSetup.LoginRateLimit)]
public partial class KayitModel(AppDbContext db, PasswordService passwords) : PageModel
{
    /// <summary>Kayıt formunun alanları.</summary>
    public sealed class RegisterInput
    {
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Password { get; set; }
        public bool Consent { get; set; }
    }

    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>Alan adı → hata metninin dil anahtarı.</summary>
    public Dictionary<string, string> Errors { get; } = [];

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // ---- Doğrulama ----
        var fullName = Input.FullName?.Trim() ?? "";
        var email = PasswordService.NormalizeEmail(Input.Email ?? "");
        if (fullName.Length == 0) Errors["FullName"] = "auth.required";
        if (!EmailPattern().IsMatch(email)) Errors["Email"] = "auth.emailInvalid";
        if ((Input.Password ?? "").Length < PasswordService.MinimumLength) Errors["Password"] = "register.passwordShort";
        if (!Input.Consent) Errors["Consent"] = "register.consentRequired";
        if (Errors.Count == 0 && await db.Customers.AnyAsync(c => c.OrganizationId == Organization.DefaultId && c.Email == email, cancellationToken))
            Errors["Email"] = "register.emailTaken";
        if (Errors.Count > 0)
            return Page();

        // ---- Kayıt ve giriş ----
        var customer = new Customer
        {
            OrganizationId = Organization.DefaultId,
            FullName = fullName[..Math.Min(fullName.Length, 200)],
            Email = email,
            Phone = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim()[..Math.Min(Input.Phone.Trim().Length, 30)],
            PasswordHash = passwords.Hash(Input.Password!),
            ConsentAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow,
        };
        db.Customers.Add(customer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Aynı anda aynı e-postayla iki kayıt: benzersiz indeks ikincisini reddeder.
            Errors["Email"] = "register.emailTaken";
            return Page();
        }

        await HttpContext.SignInAsync(AuthSchemes.Customer, Principals.ForCustomer(customer));
        return LocalRedirect(Principals.SafeReturnUrl(ReturnUrl, "/"));
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$")]
    private static partial Regex EmailPattern();
}
