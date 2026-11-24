using ConferenceRoom.Domain.Common;

namespace ConferenceRoom.Domain.Accounts;

/// <summary>
/// E-postayla gönderilen tek kullanımlık kodlar: şifre yenileme bağlantısı ve iki adımlı doğrulama (giriş) kodu.
/// Kodun kendisi saklanmaz, yalnızca özeti (<see cref="CodeHash"/>, SHA-256); veritabanını okuyan biri kodu kullanamaz.
/// Kod süresi dolunca, kullanılınca ya da aynı hesap için yenisi istenince geçersizdir.
/// </summary>
public class AccountToken : Entity, IOrganizationScoped
{
    public const int HashLength = 64;

    public int OrganizationId { get; set; }

    public AccountTokenPurpose Purpose { get; set; }

    public AccountKind AccountKind { get; set; }

    /// <summary>StaffUsers ya da Customers kimliği (<see cref="AccountKind"/>'a göre).</summary>
    public int AccountId { get; set; }

    /// <summary>Kodun SHA-256 özeti (onaltılık, küçük harf).</summary>
    public required string CodeHash { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Kullanıldığı ya da geçersiz kılındığı an; null ise hâlâ kullanılabilir (süresi dolmadıysa).</summary>
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>Yanlış deneme sayısı (giriş kodunda sınırlıdır).</summary>
    public int FailedAttempts { get; set; }
}

public enum AccountTokenPurpose
{
    /// <summary>Şifre yenileme bağlantısı (/sifre-yenile).</summary>
    PasswordReset,

    /// <summary>İki adımlı doğrulamada girişte e-postaya gönderilen 6 haneli kod.</summary>
    LoginCode,
}

public enum AccountKind
{
    Staff,
    Customer,
}
