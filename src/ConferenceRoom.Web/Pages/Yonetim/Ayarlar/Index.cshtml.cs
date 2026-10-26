using System.Security.Claims;
using ConferenceRoom.Domain.Settings;
using ConferenceRoom.Web.Auth;
using ConferenceRoom.Web.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Web.Pages.Yonetim.Ayarlar;

/// <summary>
/// Sistem ayarları (/yonetim/ayarlar, "settings.manage" yetkisi): e-posta gönderimi (SMTP) ve sitenin adresi.
/// Bu ayarlarla şifre yenileme bağlantısı ve iki adımlı doğrulama kodu gönderilir. SMTP şifresi ekrana geri gönderilmez;
/// alan boş bırakılırsa kayıtlı şifre değişmez. Kaydettikten sonra "Deneme e-postası gönder" ile ayarlar sınanır.
/// </summary>
public class IndexModel(SmtpSettingsStore store, IEmailSender email, IStringLocalizer T) : PageModel
{
    public sealed class SettingsInput
    {
        public string? Host { get; set; }
        public int? Port { get; set; } = 587;
        public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public bool ClearPassword { get; set; }
        public string? FromEmail { get; set; }
        public string? FromName { get; set; }
        public string? PublicUrl { get; set; }
    }

    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    /// <summary>Deneme e-postasının gideceği adres (varsayılan: giriş yapan kullanıcının adresi).</summary>
    [BindProperty]
    public string? TestTo { get; set; }

    /// <summary>Ayarlar kayıtlı mı (deneme e-postası ancak kayıttan sonra gönderilir).</summary>
    public bool Configured { get; private set; }

    public bool HasPassword { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>Alan adı ya da "" (formun geneli) → hata metninin dil anahtarı.</summary>
    public Dictionary<string, string> Errors { get; } = [];

    /// <summary>Deneme e-postasının sonucu: true gönderildi, false gönderilemedi (<see cref="TestError"/>), null denenmedi.</summary>
    public bool? TestSent { get; private set; }

    public string? TestError { get; private set; }

    [TempData]
    public string? Flash { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var settings = await LoadAsync(cancellationToken);
        if (settings is not null)
        {
            Input = new SettingsInput
            {
                Host = settings.Host,
                Port = settings.Port,
                Security = settings.Security,
                UserName = settings.UserName,
                FromEmail = settings.FromEmail,
                FromName = settings.FromName,
                PublicUrl = settings.PublicUrl,
            };
        }
        else
        {
            Input.PublicUrl = $"{Request.Scheme}://{Request.Host}";   // yalnızca öneri; yönetici onaylayıp kaydeder
        }
    }

    // ---- Kaydetme ----

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var host = Input.Host?.Trim() ?? "";
        var fromEmail = PasswordService.NormalizeEmail(Input.FromEmail ?? "");
        var userName = Input.UserName?.Trim();
        var fromName = Input.FromName?.Trim();
        var publicUrl = NormalizeUrl(Input.PublicUrl);
        if (host.Length is 0 or > SmtpSettings.MaxHost) Errors["Host"] = "auth.required";
        if (Input.Port is not (>= 1 and <= 65535)) Errors["Port"] = "settings.portInvalid";
        if (!Enum.IsDefined(Input.Security)) Errors["Security"] = "auth.required";
        if (!PasswordService.IsValidEmail(fromEmail) || fromEmail.Length > SmtpSettings.MaxEmail) Errors["FromEmail"] = "auth.emailInvalid";
        if (publicUrl is null) Errors["PublicUrl"] = "settings.urlInvalid";
        if (userName?.Length > SmtpSettings.MaxUserName || fromName?.Length > SmtpSettings.MaxName) Errors[""] = "admin.formErrors";
        if (Errors.Count > 0)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var values = new SmtpSettings
        {
            Host = host,
            Port = Input.Port!.Value,
            Security = Input.Security,
            FromEmail = fromEmail,
            PublicUrl = publicUrl!,
            UserName = string.IsNullOrEmpty(userName) ? null : userName,
            FromName = string.IsNullOrEmpty(fromName) ? null : fromName,
        };
        // Şifre: yazıldıysa yenisi, "şifreyi kaldır" işaretliyse silinir, ikisi de değilse kayıtlı şifre kalır.
        var newPassword = !string.IsNullOrEmpty(Input.Password) ? Input.Password : Input.ClearPassword ? "" : null;
        await store.SaveAsync(values, newPassword, User.UserId(), cancellationToken);
        Flash = "settings.saved";
        return Redirect("/yonetim/ayarlar");
    }

    // ---- Deneme e-postası ----

    public async Task<IActionResult> OnPostTestAsync(CancellationToken cancellationToken)
    {
        await OnGetAsync(cancellationToken);
        var to = PasswordService.NormalizeEmail(TestTo ?? "");
        if (!PasswordService.IsValidEmail(to))
        {
            Errors["TestTo"] = "auth.emailInvalid";
            return Page();
        }
        try
        {
            await email.SendAsync(new EmailMessage(to, T["email.testSubject"], T["email.testBody", T["app.name"]]), cancellationToken);
            TestSent = true;
        }
        catch (EmailNotConfiguredException)
        {
            TestSent = false;
            TestError = T["settings.notSaved"];
        }
        catch (EmailSendException ex)
        {
            TestSent = false;
            TestError = ex.Message;
        }
        return Page();
    }

    // ---- Yardımcılar ----

    private async Task<SmtpSettings?> LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await store.FindAsync(cancellationToken);
        Configured = settings is not null;
        HasPassword = settings?.ProtectedPassword is not null;
        UpdatedAtUtc = settings?.UpdatedAtUtc;
        TestTo ??= User.FindFirstValue(ClaimTypes.Email);
        return settings;
    }

    /// <summary>Site adresi: http/https ile başlayan mutlak adres, sonunda "/" olmadan; geçersizse null.</summary>
    private static string? NormalizeUrl(string? value)
    {
        var text = value?.Trim().TrimEnd('/') ?? "";
        return text.Length <= SmtpSettings.MaxUrl && Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && string.IsNullOrEmpty(uri.Query)
            ? text
            : null;
    }
}
