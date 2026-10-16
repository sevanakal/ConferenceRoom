using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Common;

namespace ConferenceRoom.Domain.Events;

/// <summary>
/// Etkinliğin koltuk ayarları: bilet kategorileri (ad, fiyat, renk, protokol), hangi koltuğun hangi kategoride olduğu
/// ve hangi koltukların internetten satışa/rezervasyona açık olduğu. Her etkinlikte en fazla bir kayıt.
/// Kroki gibi tek JSON olarak saklanır (<see cref="MapJson"/>, yapısı ve denetimi <see cref="SeatMap"/>); atama koltuk
/// kimliğine (kroki item <c>id</c>) göredir, blok/kat/sıra yalnızca ekranda toplu seçme kolaylığıdır.
/// Koltuk kimlikleri kroki sürümleri arasında korunduğu için aynı ayar etkinliğin tüm seanslarına uygulanır.
/// </summary>
public class EventSeatMap : Entity, IOrganizationScoped
{
    public int OrganizationId { get; set; }

    public int EventId { get; set; }

    /// <summary>Ayarların tamamı (JSON). Bkz. <see cref="SeatMap"/>.</summary>
    public required string MapJson { get; set; }

    /// <summary>Kategorisi olan koltuk sayısı (listelerde JSON'u açmadan göstermek için).</summary>
    public int AssignedSeats { get; set; }

    /// <summary>İnternetten satışa açık koltuk sayısı.</summary>
    public int OnlineSeats { get; set; }

    /// <summary>Her kayıtta bir artar. Ekran açıldıktan sonra başkası kaydettiyse kayıt reddedilir (kroki sürümündeki kural).</summary>
    public int Revision { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public int UpdatedByStaffUserId { get; set; }

    public StaffUser? UpdatedBy { get; set; }
}
