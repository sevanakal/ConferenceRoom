using ConferenceRoom.Domain.Common;

namespace ConferenceRoom.Domain.Settings;

/// <summary>
/// Kurumun e-posta gönderim (SMTP) ayarları; kurum başına tek kayıt. Şifre yenileme bağlantısı ve iki adımlı doğrulama
/// kodu bu ayarlarla gönderilir. Panelden düzenlenir (/yonetim/ayarlar, "settings.manage" yetkisi).
/// SMTP şifresi açık saklanmaz: <see cref="ProtectedPassword"/> uygulamanın şifreleme anahtarıyla (Data Protection) şifrelidir.
/// </summary>
public class SmtpSettings : Entity, IOrganizationScoped
{
    public const int MaxHost = 255, MaxUserName = 255, MaxEmail = 254, MaxName = 200, MaxUrl = 500, MaxProtected = 2000;

    public int OrganizationId { get; set; }

    public required string Host { get; set; }

    public int Port { get; set; } = 587;

    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    /// <summary>Kullanıcı adı; boşsa sunucuya kimlik doğrulamadan bağlanılır.</summary>
    public string? UserName { get; set; }

    /// <summary>Şifrelenmiş SMTP şifresi (Data Protection); yoksa null.</summary>
    public string? ProtectedPassword { get; set; }

    /// <summary>Gönderen adresi (ör. bilet@kurum.org).</summary>
    public required string FromEmail { get; set; }

    /// <summary>Gönderen adı (ör. "Konferans Salonu"); boşsa yalnızca adres görünür.</summary>
    public string? FromName { get; set; }

    /// <summary>
    /// Sitenin dışarıdan açıldığı adres (ör. https://bilet.kurum.org). E-postadaki şifre yenileme bağlantısı buna göre
    /// kurulur; isteğin Host başlığına güvenilmez (sahte bir adrese yönlenen bağlantı gönderilmesin).
    /// </summary>
    public required string PublicUrl { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public int UpdatedByStaffUserId { get; set; }
}

/// <summary>SMTP bağlantısının güvenliği.</summary>
public enum SmtpSecurity
{
    /// <summary>Şifresiz bağlantı (yalnızca kurum içi sunucular için).</summary>
    None,

    /// <summary>Düz bağlanıp STARTTLS ile şifreli devam eder (genellikle 587).</summary>
    StartTls,

    /// <summary>Baştan şifreli bağlantı (genellikle 465).</summary>
    SslOnConnect,
}
