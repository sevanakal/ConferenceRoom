using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using ConferenceRoom.Web.Sales;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceRoom.Web.Pages.Yonetim.Bildirimler;

/// <summary>
/// Bildirimler (/yonetim/bildirimler): kullanıcının son satış/rezervasyon bildirimleri, yeniden eskiye. Sayfa açılınca
/// hepsi okundu sayılır (okunmamışlar bu açılışta vurgulu görünür). Yeni bildirimler panelin her sayfasında canlı gelir
/// (wwwroot/js/bildirimler.js); tarayıcı bildirimi izni de buradan verilir. "sales.notify" yetkisi ister (Program.cs).
/// </summary>
public class IndexModel(AppDbContext db, TimeProvider clock) : PageModel
{
    /// <summary>Sayfada gösterilen en fazla bildirim.</summary>
    public const int PageSize = 100;

    public IReadOnlyList<SalesNotifier.Item> Items { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        Items = await SalesNotifier.LatestAsync(db, userId, PageSize, cancellationToken);
        await SalesNotifier.MarkAllReadAsync(db, userId, clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
}
