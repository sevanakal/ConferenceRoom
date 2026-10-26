using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using ConferenceRoom.Web.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Web.Pages.Hesap;

/// <summary>
/// Şifremi unuttum (/sifremi-unuttum): personel ya da izleyici e-postasını yazar, kayıtlıysa şifre yenileme bağlantısı
/// gönderilir (60 dakika geçerli, tek kullanımlık; /sifre-yenile). Cevap her durumda aynıdır ki sayfa "bu e-posta kayıtlı mı"
/// sorusuna cevap vermesin; gönderim hatası yalnızca günlüğe yazılır. Bağlantının adresi SMTP ayarlarındaki site
/// adresinden kurulur (isteğin Host başlığından değil). ?panel=1: yönetim girişinden gelindi (görünüm ve dönüş bağlantısı).
/// </summary>
[EnableRateLimiting(AuthenticationSetup.LoginRateLimit)]
public class SifremiUnuttumModel(AppDbContext db, AccountCodes codes, SmtpSettingsStore smtp, IEmailSender email,
    IStringLocalizer T, ILogger<SifremiUnuttumModel> logger) : PageModel
{
    [BindProperty]
    public string? Email { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Panel { get; set; }

    /// <summary>İstek alındı (e-posta kayıtlıysa bağlantı gönderildi).</summary>
    public bool Sent { get; private set; }

    public string? ErrorKey { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var address = PasswordService.NormalizeEmail(Email ?? "");
        if (!PasswordService.IsValidEmail(address))
        {
            ErrorKey = "auth.emailInvalid";
            return Page();
        }
        var settings = await smtp.FindAsync(cancellationToken);
        if (settings is null)
        {
            ErrorKey = "forgot.notConfigured";   // hesaptan bağımsız: e-posta gönderimi hiç kurulmamış
            return Page();
        }

        // ---- Kayıtlı hesaplar: personel ve izleyici (aynı adres ikisinde de olabilir; her birine kendi bağlantısı) ----
        var staff = await db.StaffUsers
            .Where(u => u.OrganizationId == Organization.DefaultId && u.Email == address && u.IsActive && u.DeletedAtUtc == null)
            .Select(u => new { u.Id, u.FullName }).FirstOrDefaultAsync(cancellationToken);
        var customer = await db.Customers
            .Where(c => c.OrganizationId == Organization.DefaultId && c.Email == address && c.IsActive)
            .Select(c => new { c.Id, c.FullName }).FirstOrDefaultAsync(cancellationToken);
        if (staff is not null)
            await SendAsync(settings.PublicUrl, address, staff.FullName, AccountKind.Staff, staff.Id, cancellationToken);
        if (customer is not null)
            await SendAsync(settings.PublicUrl, address, customer.FullName, AccountKind.Customer, customer.Id, cancellationToken);

        Sent = true;
        return Page();
    }

    private async Task SendAsync(string publicUrl, string address, string name, AccountKind kind, int id, CancellationToken cancellationToken)
    {
        var code = await codes.CreateResetAsync(kind, id, cancellationToken);
        var link = $"{publicUrl}/sifre-yenile?kod={Uri.EscapeDataString(code)}";
        var minutes = (int)AccountCodes.ResetLifetime.TotalMinutes;
        try
        {
            await email.SendAsync(new EmailMessage(address, T["email.resetSubject"], T["email.resetBody", name, link, minutes]), cancellationToken);
        }
        catch (Exception ex) when (ex is EmailNotConfiguredException or EmailSendException)
        {
            logger.LogWarning("Şifre yenileme e-postası gönderilemedi ({Kind} {Id}).", kind, id);
        }
    }
}
