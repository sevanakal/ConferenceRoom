using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Hesap;

/// <summary>
/// Şifre yenileme (/sifre-yenile?kod=...): e-postadaki bağlantıyla açılır, yeni şifre iki kez yazılır. Kod tek kullanımlıktır;
/// şifre değişince hesabın bekleyen tüm kodları (başka yenileme bağlantıları, giriş kodları) geçersiz olur.
/// Sonra hesaba uygun giriş sayfasının bağlantısı gösterilir (panel yetkisi olan personel /yonetim/giris, diğerleri /giris).
/// </summary>
[EnableRateLimiting(AuthenticationSetup.LoginRateLimit)]
public class SifreYenileModel(AppDbContext db, AccountCodes codes, PasswordService passwords) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "kod")]
    public string? Code { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? PasswordConfirm { get; set; }

    /// <summary>Bağlantı geçerli mi (geçersizse yeni bağlantı istenir).</summary>
    public bool Valid { get; private set; }

    /// <summary>Şifre değişti; gösterilecek giriş sayfası.</summary>
    public string? DoneLoginUrl { get; private set; }

    public bool ForStaff { get; private set; }

    public Dictionary<string, string> Errors { get; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var token = await codes.FindResetAsync(Code, cancellationToken);
        Valid = token is not null;
        ForStaff = token?.AccountKind == AccountKind.Staff;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var token = await codes.FindResetAsync(Code, cancellationToken);
        Valid = token is not null;
        if (token is null)
            return Page();
        ForStaff = token.AccountKind == AccountKind.Staff;

        // ---- Doğrulama ----
        if ((Password ?? "").Length < PasswordService.MinimumLength) Errors["Password"] = "users.passwordShort";
        else if (Password != PasswordConfirm) Errors["PasswordConfirm"] = "reset.mismatch";
        if (Errors.Count > 0)
            return Page();

        // ---- Kayıt: şifre, kod kullanıldı, bekleyen diğer kodlar geçersiz ----
        var hash = passwords.Hash(Password!);
        if (token.AccountKind == AccountKind.Staff)
        {
            var user = await db.StaffUsers.WithPermissions().FirstOrDefaultAsync(u => u.Id == token.AccountId && u.IsActive, cancellationToken);
            if (user is null)
            {
                Valid = false;
                return Page();
            }
            user.PasswordHash = hash;
            DoneLoginUrl = StaffAccounts.CanUsePanel(StaffAccounts.PermissionsOf(user)) ? "/yonetim/giris" : "/giris";
        }
        else
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == token.AccountId && c.IsActive, cancellationToken);
            if (customer is null)
            {
                Valid = false;
                return Page();
            }
            customer.PasswordHash = hash;
            DoneLoginUrl = "/giris";
        }
        await codes.InvalidateAllAsync(token.AccountKind, token.AccountId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Page();
    }
}
