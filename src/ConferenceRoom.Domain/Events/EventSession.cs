using ConferenceRoom.Domain.Common;
using ConferenceRoom.Domain.Halls;

namespace ConferenceRoom.Domain.Events;

/// <summary>
/// Etkinliğin bir seansı (tarih ve saat). Koltuklar seans bazında satılır.
/// Seans eklendiği anda salonun güncel kroki sürümüne bağlanır (<see cref="HallPlanVersionId"/>):
/// kroki sonradan değişse de bu seansın oturma düzeni ve satılan koltuk kimlikleri bozulmaz.
/// </summary>
public class EventSession : Entity
{
    public int EventId { get; set; }

    /// <summary>Başlangıç (UTC).</summary>
    public DateTime StartsAtUtc { get; set; }

    /// <summary>Bitiş (UTC); isteğe bağlı.</summary>
    public DateTime? EndsAtUtc { get; set; }

    /// <summary>Kapı açılış saati (UTC); isteğe bağlı.</summary>
    public DateTime? DoorsOpenAtUtc { get; set; }

    public int HallPlanVersionId { get; set; }

    public HallPlanVersion? HallPlanVersion { get; set; }
}
