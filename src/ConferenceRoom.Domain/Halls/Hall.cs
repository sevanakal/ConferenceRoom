using ConferenceRoom.Domain.Common;

namespace ConferenceRoom.Domain.Halls;

/// <summary>
/// Etkinliklerin yapıldığı salon (ör. "Ana Salon", "Fuaye"). Salonun yerleşimi (koltuklar, sahne, kapılar)
/// <see cref="HallPlanVersion"/> kayıtlarında sürüm sürüm saklanır; en yüksek sürüm numarası salonun güncel krokisidir.
/// Salonlar silinmez, pasif yapılır: geçmiş etkinlikler salonlarına ve kroki sürümlerine bağlı kalır.
/// </summary>
public class Hall : Entity, IOrganizationScoped
{
    public int OrganizationId { get; set; }

    /// <summary>Kurum içinde benzersizdir.</summary>
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Pasif salon yeni etkinliklerde seçilemez (etkinlikler eklendiğinde); krokisi düzenlenebilir kalır.</summary>
    public bool IsActive { get; set; } = true;

    public ICollection<HallPlanVersion> PlanVersions { get; set; } = [];
}
