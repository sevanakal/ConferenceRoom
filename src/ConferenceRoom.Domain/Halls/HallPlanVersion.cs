using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Common;

namespace ConferenceRoom.Domain.Halls;

/// <summary>
/// Salon krokisinin kaydedilmiş bir sürümü. Kroki editörde her kaydedildiğinde yeni bir sürüm eklenir,
/// eski sürümler hiç değişmez. Etkinlik o anki sürüme bağlanacağı için sonradan eklenen bir sandalye
/// geçmiş etkinliklerin oturma düzenini bozmaz.
/// </summary>
public class HallPlanVersion : Entity
{
    public int HallId { get; set; }

    /// <summary>Salon içinde 1'den başlayarak artan sürüm numarası.</summary>
    public int Version { get; set; }

    /// <summary>
    /// Kroki verisinin tamamı (JSON; katlar, bloklar, öğeler; ölçüler cm). Yapısı <see cref="HallPlan"/> sınıfında
    /// denetlenir. Koltuk kimlikleri (item id) sürüm içinde sabittir; bilet satışı bu kimliklere bağlanacak.
    /// </summary>
    public required string PlanJson { get; set; }

    /// <summary>Toplam kapasite (sabit koltuklar + ekstra sandalyeler). Listelerde krokiyi açmadan göstermek için.</summary>
    public int Capacity { get; set; }

    /// <summary>Sürümü kaydeden yönetim kullanıcısı.</summary>
    public int CreatedByStaffUserId { get; set; }

    public StaffUser? CreatedBy { get; set; }
}
