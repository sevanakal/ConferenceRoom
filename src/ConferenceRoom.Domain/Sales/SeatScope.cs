using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Common;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Halls;

namespace ConferenceRoom.Domain.Sales;

/// <summary>
/// Personelin satabileceği koltuklar (koltuk kapsamı). Yalnızca koltuk kısıtlaması olan personel için geçerlidir
/// (<see cref="StaffUser.SeatsRestricted"/>); kısıtlaması olmayan personel tüm koltukları satar.
///
/// Kayıt salon başınadır ve o salondaki tüm etkinliklere uygulanır (<see cref="EventId"/> null). Gerekirse tek bir etkinlik
/// için ayrı kayıt açılır (<see cref="EventId"/> dolu); o etkinlikte salon kaydının yerine bütünüyle o geçer.
/// Kroki gibi tek JSON olarak saklanır (<see cref="ScopeJson"/>, yapısı ve denetimi <see cref="SeatScopeData"/>); kapsam
/// koltuk kimliğine (kroki item <c>id</c>) göredir, blok/kat/sıra yalnızca ekranda toplu seçme kolaylığıdır.
/// </summary>
public class SeatScope : Entity, IOrganizationScoped
{
    public int OrganizationId { get; set; }

    public int HallId { get; set; }

    public Hall? Hall { get; set; }

    /// <summary>Doluysa yalnızca bu etkinliğe özel kayıt; boşsa salonun genel kaydı.</summary>
    public int? EventId { get; set; }

    public Event? Event { get; set; }

    /// <summary>Personel → satabileceği koltuklar (JSON). Bkz. <see cref="SeatScopeData"/>.</summary>
    public required string ScopeJson { get; set; }

    /// <summary>Her kayıtta bir artar. Ekran açıldıktan sonra başkası kaydettiyse kayıt reddedilir (kroki sürümündeki kural).</summary>
    public int Revision { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public int UpdatedByStaffUserId { get; set; }

    public StaffUser? UpdatedBy { get; set; }
}
