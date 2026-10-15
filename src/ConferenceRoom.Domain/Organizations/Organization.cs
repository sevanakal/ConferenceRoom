using ConferenceRoom.Domain.Common;

namespace ConferenceRoom.Domain.Organizations;

/// <summary>
/// Sistemi kullanan kurum (belediye, üniversite, kongre merkezi...).
/// Kurulumda varsayılan bir kurum (Id = 1) oluşturulur.
/// </summary>
public class Organization : Entity
{
    /// <summary>Varsayılan kurumun kimliği. Tek kurumlu kurulumda tüm veriler buna bağlanır.</summary>
    public const int DefaultId = 1;

    public required string Name { get; set; }

    /// <summary>Adres satırında ve API'de kullanılan kısa ad (ör. "kongre-merkezi"). Benzersizdir.</summary>
    public required string Slug { get; set; }

    public bool IsActive { get; set; } = true;
}
