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

    /// <summary>
    /// Bilet satışı ve rezervasyon yapma. Hangi koltukları satabileceği ayrıca kişiye göre sınırlanabilir
    /// (<see cref="StaffUser.SeatsRestricted"/>, <see cref="Sales.SeatScope"/>); bu yetki yalnızca "satış yapabilir" der.
    /// </summary>
    public const string SalesSell = "sales.sell";

    /// <summary>Satışları ve rezervasyonları görüntüleme (satış raporu: etkinlik ve seans bazında satılan, kalan koltuklar).</summary>
    public const string SalesView = "sales.view";

    /// <summary>
    /// Satış kayıtlarını düzeltme: başka personelin yaptığı satış ya da rezervasyonun ödeme durumunu değiştirme (satış raporunda).
    /// Personel kendi işleminin ödemesini bu yetki olmadan da değiştirir (/satis/satislarim).
    /// </summary>
    public const string SalesManage = "sales.manage";

    /// <summary>Bilet satışı ya da rezervasyon yapıldığında panelde bildirim alma.</summary>
    public const string SalesNotify = "sales.notify";

    /// <summary>Yönetim kullanıcıları ve rolleri.</summary>
    public const string UsersManage = "users.manage";

    /// <summary>Tüm yetkiler, panelde gösterilecek sırayla.</summary>
    public static readonly IReadOnlyList<string> All = [HallsManage, EventsManage, PricingManage, SalesSell, SalesView, SalesManage, SalesNotify, UsersManage];
}
