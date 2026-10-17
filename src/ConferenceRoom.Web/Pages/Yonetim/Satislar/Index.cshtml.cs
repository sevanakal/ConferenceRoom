using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Common;
using ConferenceRoom.Web.Sales;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Web.Pages.Yonetim.Satislar;

/// <summary>
/// Satış raporu (/yonetim/satislar): seans bazında satılabilir, satılan, rezerve edilen ve kalan koltuklar, tahsil edilen ve
/// bekleyen ödeme. Hesaplar Web/Sales/SalesReport.cs içinde. Varsayılan olarak bitmemiş seanslar listelenir, "?gecmis=true"
/// bitmişleri de gösterir. Her satırdan seansın işlem listesine (Seans.cshtml) gidilir. "sales.view" yetkisi ister (Program.cs);
/// sayfa yalnızca okur, satış yapmaz. "?handler=Excel" aynı listeyi Excel dosyası olarak indirir (Web/Sales/SalesExcel.cs).
/// </summary>
public class IndexModel(AppDbContext db, TimeProvider clock, IStringLocalizer localizer) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "gecmis")]
    public bool IncludePast { get; set; }

    public IReadOnlyList<SalesReport.SessionRow> Sessions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Sessions = await SalesReport.SessionsAsync(db, IncludePast, clock.GetUtcNow().UtcDateTime, cancellationToken);

    /// <summary>Ekrandaki listeyi (aynı "gecmis" seçimiyle) .xlsx olarak indirir.</summary>
    public async Task<IActionResult> OnGetExcelAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var sessions = await SalesReport.SessionsAsync(db, IncludePast, now, cancellationToken);
        return File(SalesExcel.Sessions(sessions, localizer), SalesExcel.ContentType, $"satis-raporu-{DisplayTime.ToLocal(now):yyyy-MM-dd}.xlsx");
    }
}
