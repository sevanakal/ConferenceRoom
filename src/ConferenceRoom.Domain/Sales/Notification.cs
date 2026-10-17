using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Common;

namespace ConferenceRoom.Domain.Sales;

/// <summary>
/// Bir yönetim kullanıcısına giden satış bildirimi: bir satış ya da rezervasyon yapıldı (<see cref="OrderId"/>).
/// "Satış bildirimlerini al" yetkisi (<see cref="Permissions.SalesNotify"/>) olan her aktif kullanıcı için ayrı kayıt açılır;
/// metin kayıtta saklanmaz, gösterilirken işlemden ve kullanıcının dilinden üretilir. Okununca <see cref="ReadAtUtc"/> dolar.
/// </summary>
public class Notification : Entity, IOrganizationScoped
{
    public int OrganizationId { get; set; }

    public int StaffUserId { get; set; }

    public int OrderId { get; set; }

    public Order? Order { get; set; }

    public DateTime? ReadAtUtc { get; set; }
}
