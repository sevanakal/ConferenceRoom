using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Sales;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceRoom.Web.Pages.Yonetim.Satislar;

/// <summary>
/// Satış raporu (/yonetim/satislar): seans bazında satılabilir, satılan, rezerve edilen ve kalan koltuklar, tahsil edilen ve
/// bekleyen ödeme. Hesaplar Web/Sales/SalesReport.cs içinde. Varsayılan olarak bitmemiş seanslar listelenir, "?gecmis=true"
/// bitmişleri de gösterir. Her satırdan seansın işlem listesine (Seans.cshtml) gidilir. "sales.view" yetkisi ister (Program.cs);
/// sayfa yalnızca okur, satış yapmaz.
/// </summary>
public class IndexModel(AppDbContext db, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "gecmis")]
    public bool IncludePast { get; set; }

    public IReadOnlyList<SalesReport.SessionRow> Sessions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Sessions = await SalesReport.SessionsAsync(db, IncludePast, clock.GetUtcNow().UtcDateTime, cancellationToken);
}
