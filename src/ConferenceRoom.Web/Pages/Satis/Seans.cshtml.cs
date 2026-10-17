using System.Globalization;
using ConferenceRoom.Domain.Sales;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Localization;
using ConferenceRoom.Web.Auth;
using ConferenceRoom.Web.Common;
using ConferenceRoom.Web.Sales;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Web.Pages.Satis;

/// <summary>
/// Personel satış ekranı (/satis/seans/{seansId}): krokiden koltuk seçilir, alıcının adı (zorunlu), telefonu ve açıklaması
/// girilir, toplam 0'dan büyükse ödemenin alınıp alınmadığı işaretlenir. Kurallar ve kayıt Web/Sales/SalesDesk.cs içinde;
/// ekran wwwroot/js/satis-ekrani.js (kroki ve seçim koltuk seçicinin ortak çatısından, yalnızca seçtirir).
/// Satış bölümünün çereziyle açılır (satış yetkisi, Program.cs).
/// </summary>
public class SeansModel(AppDbContext db, LanguageCatalog catalog, IStringLocalizer localizer, IAntiforgery antiforgery, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int SessionId { get; set; }

    public SalesDesk.Snapshot Snapshot { get; private set; } = null!;

    /// <summary>Ekranın ayarları ve metinleri (JSON). Sayfada &lt;script type="application/json"&gt; içinde durur.</summary>
    public object EditorConfig { get; private set; } = new();

    /// <summary>Ekranın verisi: koltuk ayarları, satılmış ve personelin satabileceği koltuklar.</summary>
    public object SeatData { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var snapshot = await SalesDesk.LoadAsync(db, User.UserId(), SessionId, Now, cancellationToken);
        if (snapshot is null)
            return NotFound();
        Snapshot = snapshot;
        EditorConfig = new
        {
            culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            kind = snapshot.Session.Kind.ToString(),
            isOpen = snapshot.Session.IsOpen,
            sellUrl = $"/satis/seans/{SessionId}?handler=Sell",
            token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken,
            maxSeats = SalesDesk.MaxSeatsPerOrder,
            limits = new { name = Order.MaxBuyerName, phone = Order.MaxBuyerPhone, note = Order.MaxNote },
            texts = ScreenData.Texts(catalog, "seatEditor", "sales"),
        };
        SeatData = new { map = System.Text.Json.Nodes.JsonNode.Parse(snapshot.MapJson), sold = snapshot.Sold, allowed = snapshot.Allowed };
        return Page();
    }

    /// <summary>
    /// Satış ya da rezervasyon. Gövde: { seats[], buyerName, buyerPhone, note, paid }.
    /// Cevap: { status: sold | invalid | taken | closed, message?, orderId?, count?, sold[] } — sold: seansın güncel satılmış koltukları.
    /// </summary>
    public async Task<IActionResult> OnPostSellAsync([FromBody] SalesDesk.SellRequest request, CancellationToken cancellationToken)
    {
        var result = await SalesDesk.SellAsync(db, User.UserId(), SessionId, request, Now, cancellationToken);
        var message = result.Outcome switch
        {
            SalesDesk.Outcome.Sold => localizer[result.Order!.Kind == OrderKind.Sale ? "sales.soldToast" : "sales.reservedToast",
                result.Order.Tickets.Count, result.Order.Id].Value,
            SalesDesk.Outcome.Taken => localizer["sales.taken", string.Join(", ", result.Taken ?? [])].Value,
            SalesDesk.Outcome.Closed => localizer["sales.closed"].Value,
            _ => localizer[result.ErrorKey!].Value,
        };
        return new JsonResult(new
        {
            status = result.Outcome.ToString().ToLowerInvariant(),
            message,
            orderId = result.Order?.Id,
            taken = result.Taken,
            sold = await SalesDesk.SoldSeatsAsync(db, SessionId, cancellationToken),
        })
        {
            StatusCode = result.Outcome switch
            {
                SalesDesk.Outcome.Sold => StatusCodes.Status200OK,
                SalesDesk.Outcome.Taken => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            },
        };
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
