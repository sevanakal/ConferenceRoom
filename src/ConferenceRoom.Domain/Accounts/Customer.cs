using ConferenceRoom.Domain.Common;

namespace ConferenceRoom.Domain.Accounts;

/// <summary>
/// Bilet alan izleyici hesabı. Yönetim kullanıcılarından (<see cref="StaffUser"/>) tamamen ayrıdır:
/// kendi giriş sayfası (/giris) ve çerezi vardır, yönetim paneline giremez.
/// </summary>
public class Customer : Entity, IOrganizationScoped
{
    public int OrganizationId { get; set; }

    /// <summary>Giriş adı. Küçük harfe çevrilmiş olarak saklanır, kurum içinde benzersizdir.</summary>
    public required string Email { get; set; }

    public required string FullName { get; set; }

    public string? Phone { get; set; }

    /// <summary>ASP.NET Core PasswordHasher çıktısı (PBKDF2).</summary>
    public required string PasswordHash { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>KVKK aydınlatma metninin onaylandığı an.</summary>
    public DateTime ConsentAtUtc { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }
}
