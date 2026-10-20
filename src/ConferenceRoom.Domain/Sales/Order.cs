using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Common;
using ConferenceRoom.Domain.Events;

namespace ConferenceRoom.Domain.Sales;

/// <summary>
/// Bir satış ya da rezervasyon işlemi: bir seansta, bir alıcı için bir ya da birden çok koltuk (<see cref="Tickets"/>).
/// Türü (satış / rezervasyon) işlem anındaki etkinliğin satış türünden gelir. Personel satışında alıcının adı zorunludur,
/// telefon ve açıklama isteğe bağlıdır. Fiyat, satış anındaki koltuk kategorisinden kopyalanır; etkinliğin fiyatı
/// sonradan değişse de satılmış bilet değişmez.
/// </summary>
public class Order : Entity, IOrganizationScoped
{
    public const int MaxBuyerName = 100, MaxBuyerPhone = 30, MaxNote = 500;

    public int OrganizationId { get; set; }

    public int EventId { get; set; }

    public Event? Event { get; set; }

    public int EventSessionId { get; set; }

    public EventSession? EventSession { get; set; }

    public OrderKind Kind { get; set; }

    public OrderChannel Channel { get; set; }

    public required string BuyerName { get; set; }

    public string? BuyerPhone { get; set; }

    public string? Note { get; set; }

    /// <summary>Biletlerin toplamı (TL).</summary>
    public decimal Total { get; set; }

    /// <summary>Ödeme durumu; toplam 0 ise ödeme sorulmaz (<see cref="PaymentStatus.NotRequired"/>).</summary>
    public PaymentStatus Payment { get; set; }

    /// <summary>İşlemi yapan personel (personel satışında dolu).</summary>
    public int? SoldByStaffUserId { get; set; }

    public StaffUser? SoldBy { get; set; }

    public ICollection<Ticket> Tickets { get; set; } = [];
}

/// <summary>
/// Satılmış ya da rezerve edilmiş tek koltuk. Koltuk, seansın bağlı olduğu kroki sürümündeki koltuk kimliğiyle
/// (<see cref="SeatId"/>) tutulur; ekranda okunacak ad (<see cref="SeatLabel"/>, ör. "A-12"), kategori ve fiyat satış anındaki
/// hâliyle kopyalanır. Aynı seansta bir koltuk bir kez satılır (veritabanında benzersiz indeks).
/// <see cref="CategoryId"/> satıldığı kategorinin kimliğidir: bileti satılmış kategori koltuk ayarlarından silinemez.
/// </summary>
public class Ticket : Entity, IOrganizationScoped
{
    public const int MaxSeatId = 100, MaxSeatLabel = 40, MaxCategoryName = SeatMap.MaxNameLength, MaxCategoryId = SeatMap.MaxCategoryIdLength;

    public int OrganizationId { get; set; }

    public int OrderId { get; set; }

    public int EventSessionId { get; set; }

    public required string SeatId { get; set; }

    public required string SeatLabel { get; set; }

    /// <summary>Satıldığı kategorinin kimliği (koltuk ayarlarındaki "id"). Bu alan eklenmeden önce satılmış biletlerde boş.</summary>
    public string? CategoryId { get; set; }

    public required string CategoryName { get; set; }

    public decimal Price { get; set; }
}

/// <summary>İşlemin türü: etkinliğin satış türünden (<see cref="SaleMode"/>) gelir.</summary>
public enum OrderKind
{
    Sale,
    Reservation,
}

/// <summary>İşlemin yapıldığı yer: gişede personel ya da internetten izleyici.</summary>
public enum OrderChannel
{
    Staff,
    Online,
}

/// <summary>Ödeme durumu. Sanal POS gibi bir entegrasyon henüz yok; personel ödemeyi alıp almadığını işaretler.</summary>
public enum PaymentStatus
{
    /// <summary>Toplam 0 (ücretsiz ya da yalnızca rezervasyon); ödeme sorulmaz.</summary>
    NotRequired,

    Received,

    Pending,
}
