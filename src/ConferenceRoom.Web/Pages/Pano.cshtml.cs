using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Common;
using ConferenceRoom.Web.Events;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceRoom.Web.Pages;

/// <summary>
/// Herkese açık etkinlik panosu: sitenin ana sayfası (/, Program.cs'teki ek yol) ve lobideki ekran için /pano.
/// Giriş gerektirmez. Lobideki bir ekranda tam ekran açık durabilsin diye kendi sade şablonu vardır ve dakikada bir
/// kendini yeniler. Seanslar güne göre gruplanır (Türkiye saatiyle).
/// </summary>
public class PanoModel(AppDbContext db, TimeProvider clock, ILogger<PanoModel> logger) : PageModel
{
    public sealed record Day(DateOnly Date, IReadOnlyList<BoardQuery.Item> Items);

    public IReadOnlyList<Day> Days { get; private set; } = [];

    /// <summary>Bugünün tarihi (Türkiye saatiyle); "Bugün" / "Yarın" başlıkları için.</summary>
    public DateOnly Today { get; private set; }

    /// <summary>Sayfanın hazırlandığı an, Türkiye saatiyle (üstteki saat).</summary>
    public DateTime NowLocal { get; private set; }

    /// <summary>Veritabanına ulaşılamadıysa false; sayfa yine açılır, bir uyarı gösterir.</summary>
    public bool Available { get; private set; } = true;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        NowLocal = DisplayTime.ToLocal(now);
        Today = DateOnly.FromDateTime(NowLocal);
        IReadOnlyList<BoardQuery.Item> items;
        try
        {
            items = await BoardQuery.LoadAsync(db, now, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Pano için veritabanına bağlanılamadı.");
            Available = false;
            return;
        }
        Days = items
            .GroupBy(i => DateOnly.FromDateTime(DisplayTime.ToLocal(i.StartsAtUtc)))
            .Select(g => new Day(g.Key, g.ToList()))
            .ToList();
    }
}
