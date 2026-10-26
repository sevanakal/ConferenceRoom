using ConferenceRoom.Domain.Common;

namespace ConferenceRoom.Domain.Accounts;

/// <summary>
/// Yönetim paneline giren kullanıcı (kurum çalışanı). Bilet alan izleyiciler ayrı tutulur
/// (<see cref="Customer"/>): iki tarafın giriş sayfası, çerezi ve tablosu ayrıdır.
/// Yetkileri sahip olduğu rollerden gelir.
/// </summary>
public class StaffUser : Entity, IOrganizationScoped
{
    public int OrganizationId { get; set; }

    /// <summary>Giriş adı. Küçük harfe çevrilmiş olarak saklanır, kurum içinde benzersizdir.</summary>
    public required string Email { get; set; }

    public required string FullName { get; set; }

    /// <summary>ASP.NET Core PasswordHasher çıktısı (PBKDF2). Şifrenin kendisi hiçbir yerde saklanmaz.</summary>
    public required string PasswordHash { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Silinme zamanı; null ise kullanıcı silinmemiştir. Silinen kullanıcının satır kaydı kalır ki geçmiş satışlarda, krokilerde
    /// ve etkinliklerde kimin ne yaptığı (adı) görünmeye devam etsin; ama listede görünmez, giremez, rolleri ve e-postası
    /// kaldırılır (e-posta yeni bir kullanıcıya verilebilsin diye). Bkz. StaffAccounts.DeleteAsync.
    /// </summary>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>
    /// Koltuk kısıtlaması. false: satış yetkisi varsa tüm koltukları satar (tam yetkili). true: yalnızca satış yetkileri
    /// ekranında kendisine verilen koltukları satar (<see cref="Sales.SeatScope"/>); verilmemişse o salonda hiç satamaz.
    /// </summary>
    public bool SeatsRestricted { get; set; }

    /// <summary>
    /// İki adımlı doğrulama: true ise şifreden sonra e-postasına gönderilen 6 haneli kod da istenir (yönetim ve satış girişi).
    /// Kullanıcı başına yönetimden açılır; açmak için SMTP ayarlarının yapılmış olması gerekir.
    /// </summary>
    public bool TwoFactorEnabled { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>
    /// Kullanıcının panel (/yonetim) kart sırası: kart anahtarları virgülle ayrılmış (ör. "events.manage,halls.manage").
    /// null: varsayılan sıra. Listede olmayan kartlar (sonradan eklenenler) sona varsayılan sırayla eklenir.
    /// </summary>
    public string? DashboardOrder { get; set; }

    public ICollection<StaffUserRole> Roles { get; set; } = [];
}

/// <summary>Kullanıcı ile rol arasındaki bağ. Bir kullanıcının birden fazla rolü olabilir.</summary>
public class StaffUserRole
{
    public int StaffUserId { get; set; }

    public int RoleId { get; set; }

    public Role? Role { get; set; }
}
