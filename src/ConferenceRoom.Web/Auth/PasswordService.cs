using Microsoft.AspNetCore.Identity;

namespace ConferenceRoom.Web.Auth;

/// <summary>
/// Şifre özeti üretme ve doğrulama. ASP.NET Core'un PasswordHasher sınıfını (PBKDF2, tuzlu) kullanır;
/// ASP.NET Identity'nin geri kalanına (tablolar, UserManager) ihtiyaç duymadan.
/// </summary>
public sealed class PasswordService
{
    /// <summary>Kayıt ve şifre değiştirmede istenen en kısa şifre uzunluğu.</summary>
    public const int MinimumLength = 8;

    private readonly PasswordHasher<object> _hasher = new();

    // Bulunamayan kullanıcı için de bir doğrulama yapılır ki yanıt süresinden "bu e-posta kayıtlı mı" anlaşılmasın.
    private readonly string _dummyHash;

    public PasswordService() => _dummyHash = Hash(Guid.NewGuid().ToString());

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    /// <summary>Şifre doğruysa true. hash null ise (kullanıcı yok) yine aynı sürede false döner.</summary>
    public bool Verify(string? hash, string password)
    {
        var result = _hasher.VerifyHashedPassword(null!, hash ?? _dummyHash, password);
        return hash is not null && result != PasswordVerificationResult.Failed;
    }

    /// <summary>E-postalar tek biçimde saklanır ve aranır: boşluksuz, küçük harf.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
