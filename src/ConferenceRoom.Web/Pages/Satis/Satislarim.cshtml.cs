using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using ConferenceRoom.Web.Sales;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Web.Pages.Satis;

/// <summary>
/// Satışlarım (/satis/satislarim): satış personelinin kendi yaptığı satış ve rezervasyonlar, yeniden eskiye (kime, hangi
/// koltuklar, tutar, ödeme). "?seans={id}" yalnızca o seansın işlemlerini gösterir. Ödemesi alınmamış işlem sonradan
/// "ödeme alındı" yapılabilir (ya da yanlış işaretleme geri alınır); kural Web/Sales/SalesDesk.SetPaymentAsync içinde.
/// Satış bölümünün çereziyle açılır (Program.cs).
/// </summary>
public class SatislarimModel(AppDbContext db, IStringLocalizer localizer) : PageModel
{
    /// <summary>Listede gösterilen en fazla işlem.</summary>
    public const int Limit = 300;

    [BindProperty(SupportsGet = true, Name = "seans")]
    public int? SessionId { get; set; }

    public IReadOnlyList<SalesReport.OrderRow> Orders { get; private set; } = [];

    [TempData]
    public string? Flash { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        var mine = db.Orders.Where(o => o.SoldByStaffUserId == userId && (SessionId == null || o.EventSessionId == SessionId));
        Orders = await SalesReport.OrdersAsync(mine, Limit, cancellationToken);
    }

    /// <summary>Ödeme durumunu değiştirir ve listeye (aynı süzgeçle, işlemin satırına) döner.</summary>
    public async Task<IActionResult> OnPostPaymentAsync(int orderId, bool paid, CancellationToken cancellationToken)
    {
        if (await SalesDesk.SetPaymentAsync(db, User.UserId(), orderId, paid, cancellationToken))
            Flash = localizer[paid ? "myOrders.markedPaid" : "myOrders.markedUnpaid", orderId];
        return Redirect($"/satis/satislarim{(SessionId is { } s ? $"?seans={s}" : "")}#islem-{orderId}");
    }
}
