using ConferenceRoom.Domain.Common;

namespace ConferenceRoom.Domain.Events;

/// <summary>
/// Etkinlik türü (Konser, Konferans, Tiyatro...). Etkinlik oluşturulurken seçilir ve bazı varsayılanları taşır:
/// etkinlik bu değerleri devralır, tek tek etkinlikte değiştirilebilir. Türler silinmez, pasif yapılır.
/// </summary>
public class EventType : Entity, IOrganizationScoped
{
    public const int ConferenceId = 1, ConcertId = 2, TheatreId = 3, SeminarId = 4, MeetingId = 5;

    public int OrganizationId { get; set; }

    /// <summary>Kurum içinde benzersizdir.</summary>
    public required string Name { get; set; }

    /// <summary>Varsayılan satış türü: rezervasyon mu, bilet satışı mı.</summary>
    public SaleMode DefaultSaleMode { get; set; } = SaleMode.Reservation;

    /// <summary>Varsayılan onay kuralı: rezervasyon yönetici onayından sonra mı kesinleşir, yoksa anında mı.</summary>
    public bool DefaultRequiresApproval { get; set; }

    /// <summary>Pasif tür yeni etkinliklerde seçilemez; eski etkinlikler türüne bağlı kalır.</summary>
    public bool IsActive { get; set; } = true;
}
