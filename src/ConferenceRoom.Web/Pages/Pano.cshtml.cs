using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Common;
using ConferenceRoom.Web.Events;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceRoom.Web.Pages;

/// <summary>
/// Herkese açık etkinlik panosu (/pano): giriş gerektirmez. Lobideki bir ekranda tam ekran açık durabilsin diye
/// kendi sade şablonu vardır ve dakikada bir kendini yeniler. Seanslar güne göre gruplanır (Türkiye saatiyle).
/// </summary>
public class PanoModel(AppDbContext db, TimeProvider clock) : PageModel
{
    public sealed record Day(DateOnly Date, IReadOnlyList<BoardQuery.Item> Items);

    public IReadOnlyList<Day> Days { get; private set; } = [];

    /// <summary>Bugünün tarihi (Türkiye saatiyle); "Bugün" / "Yarın" başlıkları için.</summary>
    public DateOnly Today { get; private set; }

    /// <summary>Sayfanın hazırlandığı an, Türkiye saatiyle (üstteki saat).</summary>
    public DateTime NowLocal { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        NowLocal = DisplayTime.ToLocal(now);
        Today = DateOnly.FromDateTime(NowLocal);
        var items = await BoardQuery.LoadAsync(db, now, cancellationToken);
        Days = items
            .GroupBy(i => DateOnly.FromDateTime(DisplayTime.ToLocal(i.StartsAtUtc)))
            .Select(g => new Day(g.Key, g.ToList()))
            .ToList();
    }
}
