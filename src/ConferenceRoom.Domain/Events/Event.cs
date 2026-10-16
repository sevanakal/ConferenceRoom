using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Common;
using ConferenceRoom.Domain.Halls;

namespace ConferenceRoom.Domain.Events;

/// <summary>
/// Etkinlik: bir salonda yapılan, bir ya da birden çok seansı olan gösteri, konser, konferans...
/// Bilgiler (ad, açıklama, afiş, satış kuralları) bir kez girilir; aynı oyun 5 gün oynanacaksa 5 <see cref="EventSession"/> eklenir
/// ve koltuklar her seans için ayrı satılır. Etkinlikler silinmez; vazgeçilen etkinlik "İptal" durumuna alınır.
/// </summary>
public class Event : Entity, IOrganizationScoped
{
    public int OrganizationId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    /// <summary>Düzenleyen kurum ya da kişi (ör. "Şehir Tiyatroları"); isteğe bağlı.</summary>
    public string? Organizer { get; set; }

    public int EventTypeId { get; set; }

    public EventType? EventType { get; set; }

    public int HallId { get; set; }

    public Hall? Hall { get; set; }

    /// <summary>Afiş görselinin yükleme klasöründeki dosya adı (ör. "3f2a....webp"); afiş yoksa null.</summary>
    public string? PosterFileName { get; set; }

    public EventStatus Status { get; set; } = EventStatus.Draft;

    // ---- Satış kuralları (etkinlik türünden varsayılan gelir) ----

    public SaleMode SaleMode { get; set; } = SaleMode.Reservation;

    /// <summary>Rezervasyon yönetici onayından sonra mı kesinleşir.</summary>
    public bool RequiresApproval { get; set; }

    /// <summary>İzleyiciler internetten bilet alabilir / rezervasyon yapabilir mi. Kapalıysa yalnızca personel satar.</summary>
    public bool OnlineEnabled { get; set; } = true;

    /// <summary>Satışın açılacağı an (UTC); null ise yayınlandığı anda açılır.</summary>
    public DateTime? SalesStartUtc { get; set; }

    /// <summary>Satışın kapanacağı an (UTC); null ise her seans başlayınca kendi satışı kapanır.</summary>
    public DateTime? SalesEndUtc { get; set; }

    /// <summary>Bir kişinin bir seans için alabileceği en fazla bilet; null ise sınır yok.</summary>
    public int? MaxTicketsPerPerson { get; set; }

    public int CreatedByStaffUserId { get; set; }

    public StaffUser? CreatedBy { get; set; }

    public ICollection<EventSession> Sessions { get; set; } = [];
}

/// <summary>Etkinliğin yaşam döngüsü. "Tamamlandı" saklanmaz: tüm seansları geçmiş yayındaki etkinlik tamamlanmış sayılır.</summary>
public enum EventStatus
{
    /// <summary>Hazırlanıyor; panoda ve satışta görünmez.</summary>
    Draft,

    /// <summary>Panoda görünür; satış kurallarına göre satılır.</summary>
    Published,

    /// <summary>Panoda görünür ama yeni satış/rezervasyon alınmaz.</summary>
    SalesClosed,

    /// <summary>İptal edildi; panoda "İptal" olarak görünür.</summary>
    Cancelled,
}

/// <summary>Satış türü: yer ayırtma (ödeme yok ya da sonra) ya da bilet satışı.</summary>
public enum SaleMode
{
    Reservation,
    Sale,
}
