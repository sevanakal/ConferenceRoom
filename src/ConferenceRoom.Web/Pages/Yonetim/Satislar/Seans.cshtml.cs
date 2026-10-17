using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Sales;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceRoom.Web.Pages.Yonetim.Satislar;

/// <summary>
/// Seansın satış ayrıntısı (/yonetim/satislar/seans/{id}): raporun o seansa ait satırı ve işlem listesi (zaman, alıcı,
/// telefon, koltuklar, tutar, ödeme, satan personel, açıklama). Bildirimlerden de buraya gelinir. "sales.view" yetkisi ister.
/// </summary>
public class SeansModel(AppDbContext db, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public SalesReport.SessionRow Session { get; private set; } = null!;

    public IReadOnlyList<SalesReport.OrderRow> Orders { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var row = (await SalesReport.SessionsAsync(db, includePast: true, clock.GetUtcNow().UtcDateTime, cancellationToken, sessionId: Id)).SingleOrDefault();
        if (row is null)
            return NotFound();
        Session = row;
        Orders = await SalesReport.OrdersAsync(db, Id, cancellationToken);
        return Page();
    }
}
