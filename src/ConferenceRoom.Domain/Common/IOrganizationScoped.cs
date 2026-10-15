namespace ConferenceRoom.Domain.Common;

/// <summary>
/// Bir kuruma ait olan varlıkları işaretler. Sistem şimdilik tek kurumla çalışıyor,
/// ama salon, etkinlik, rezervasyon gibi her kurum verisi bu arayüzü uygular.
/// Böylece ileride çok kuruma geçerken tablo yapısı değişmez; yalnızca sorgulara
/// kurum filtresi eklenir.
/// </summary>
public interface IOrganizationScoped
{
    int OrganizationId { get; set; }
}
