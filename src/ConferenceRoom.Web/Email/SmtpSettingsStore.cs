using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Domain.Settings;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Email;

/// <summary>
/// SMTP ayarlarının okunması ve kaydı. SMTP şifresi veritabanına şifrelenerek yazılır (Data Protection; anahtarlar da
/// veritabanında, bkz. AppDbContext); yalnızca gönderim anında çözülür, ekrana hiç geri gönderilmez.
/// </summary>
public sealed class SmtpSettingsStore(AppDbContext db, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("ConferenceRoom.SmtpPassword");

    /// <summary>Kurumun kayıtlı ayarları; henüz yapılmadıysa null.</summary>
    public Task<SmtpSettings?> FindAsync(CancellationToken cancellationToken) =>
        db.SmtpSettings.FirstOrDefaultAsync(s => s.OrganizationId == Organization.DefaultId, cancellationToken);

    /// <summary>E-posta gönderilebilir mi (ayarlar yapılmış mı). İki adımlı doğrulamayı açmadan önce denetlenir.</summary>
    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) =>
        db.SmtpSettings.AnyAsync(s => s.OrganizationId == Organization.DefaultId, cancellationToken);

    /// <summary>Şifreyi çözer; şifre yoksa ya da anahtar değiştiği için çözülemiyorsa null.</summary>
    public string? PasswordOf(SmtpSettings settings)
    {
        if (settings.ProtectedPassword is null)
            return null;
        try
        {
            return _protector.Unprotect(settings.ProtectedPassword);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;   // anahtarlar kaybolduysa şifre yeniden girilmeli; gönderim kimlik doğrulama hatası verir
        }
    }

    /// <summary>
    /// Ayarları kaydeder. <paramref name="newPassword"/>: null ise kayıtlı şifre değişmez, boş metin şifreyi siler.
    /// </summary>
    public async Task SaveAsync(SmtpSettings values, string? newPassword, int staffUserId, CancellationToken cancellationToken)
    {
        var settings = await FindAsync(cancellationToken);
        if (settings is null)
        {
            settings = new SmtpSettings { OrganizationId = Organization.DefaultId, Host = "", FromEmail = "", PublicUrl = "" };
            db.SmtpSettings.Add(settings);
        }
        settings.Host = values.Host;
        settings.Port = values.Port;
        settings.Security = values.Security;
        settings.UserName = values.UserName;
        settings.FromEmail = values.FromEmail;
        settings.FromName = values.FromName;
        settings.PublicUrl = values.PublicUrl;
        if (newPassword is not null)
            settings.ProtectedPassword = newPassword.Length == 0 ? null : _protector.Protect(newPassword);
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByStaffUserId = staffUserId;
        await db.SaveChangesAsync(cancellationToken);
    }
}
