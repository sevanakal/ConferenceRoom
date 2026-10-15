namespace ConferenceRoom.Domain.Accounts;

/// <summary>
/// Yönetim panelindeki yetkilerin listesi. Yetki kodları kodda sabittir, çünkü kod hangi işlemin
/// hangi yetkiyi istediğini bilmek zorundadır. Hangi rolün hangi yetkilere sahip olduğu ise
/// veritabanındadır (<see cref="RolePermission"/>): yeni rol kod değiştirmeden eklenir.
///
/// Yeni yetki eklerken: buraya sabit + <see cref="All"/> listesine ekle, dil dosyalarına
/// "permission.&lt;kod&gt;.name" ve ".description" metinlerini yaz. Testler eksik metni yakalar.
/// </summary>
public static class Permissions
{
    /// <summary>Salon ekleme ve salon krokisi çizme/düzenleme.</summary>
    public const string HallsManage = "halls.manage";

    /// <summary>Etkinlik oluşturma, salona ve kroki sürümüne bağlama.</summary>
    public const string EventsManage = "events.manage";

    /// <summary>Bilet fiyatları, fiyat grupları ve koltuk kategorileri.</summary>
    public const string PricingManage = "pricing.manage";

    /// <summary>Satışları ve rezervasyonları görüntüleme.</summary>
    public const string SalesView = "sales.view";

    /// <summary>Yönetim kullanıcıları ve rolleri.</summary>
    public const string UsersManage = "users.manage";

    /// <summary>Tüm yetkiler, panelde gösterilecek sırayla.</summary>
    public static readonly IReadOnlyList<string> All = [HallsManage, EventsManage, PricingManage, SalesView, UsersManage];
}
