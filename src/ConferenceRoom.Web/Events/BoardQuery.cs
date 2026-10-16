using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Events;

/// <summary>
/// Herkese açık etkinlik panosunun (/pano) verisi: bitmemiş seanslar, başlangıç saatine göre.
/// Taslak etkinlikler görünmez; iptal edilenler "İptal edildi" olarak görünür ki gelen kişi haberdar olsun.
/// Her seans için panoda gösterilecek tek bir durum hesaplanır (<see cref="BoardBadge"/>).
/// </summary>
public static class BoardQuery
{
    /// <summary>Bitiş saati yazılmamış seans başladıktan bu kadar sonra panodan düşer.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(2);

    public sealed record Item(
        int SessionId, DateTime StartsAtUtc, DateTime? EndsAtUtc, DateTime? DoorsOpenAtUtc,
        string Title, string? Organizer, string TypeName, string HallName, string? PosterFileName,
        BoardBadge Badge, DateTime? SalesStartUtc);

    public static async Task<IReadOnlyList<Item>> LoadAsync(AppDbContext db, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var fallbackStart = nowUtc - DefaultDuration;
        var rows = await db.EventSessions
            .Join(db.Events, s => s.EventId, e => e.Id, (s, e) => new { s, e })
            .Where(x => x.e.OrganizationId == Organization.DefaultId && x.e.Status != EventStatus.Draft)
            .Where(x => x.s.EndsAtUtc != null ? x.s.EndsAtUtc > nowUtc : x.s.StartsAtUtc > fallbackStart)
            .OrderBy(x => x.s.StartsAtUtc)
            .Select(x => new
            {
                x.s.Id,
                x.s.StartsAtUtc,
                x.s.EndsAtUtc,
                x.s.DoorsOpenAtUtc,
                x.e.Title,
                x.e.Organizer,
                TypeName = x.e.EventType!.Name,
                HallName = x.e.Hall!.Name,
                x.e.PosterFileName,
                x.e.Status,
                x.e.SaleMode,
                x.e.OnlineEnabled,
                x.e.SalesStartUtc,
                x.e.SalesEndUtc,
            })
            .Take(200)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new Item(
            r.Id, r.StartsAtUtc, r.EndsAtUtc, r.DoorsOpenAtUtc, r.Title, r.Organizer, r.TypeName, r.HallName, r.PosterFileName,
            BadgeOf(r.Status, r.SaleMode, r.OnlineEnabled, r.SalesStartUtc, r.SalesEndUtc, r.StartsAtUtc, nowUtc), r.SalesStartUtc)).ToList();
    }

    /// <summary>Panodaki durum etiketi; öncelik sırası: iptal, şu anda, satış kapalı, satış henüz açılmadı, açık.</summary>
    public static BoardBadge BadgeOf(EventStatus status, SaleMode mode, bool online, DateTime? salesStartUtc, DateTime? salesEndUtc, DateTime startsAtUtc, DateTime nowUtc)
    {
        if (status == EventStatus.Cancelled)
            return BoardBadge.Cancelled;
        if (startsAtUtc <= nowUtc)
            return BoardBadge.Now;
        if (status == EventStatus.SalesClosed || salesEndUtc <= nowUtc)
            return BoardBadge.SalesClosed;
        if (salesStartUtc > nowUtc)
            return BoardBadge.SalesSoon;
        if (!online)
            return BoardBadge.BoxOffice;
        return mode == SaleMode.Sale ? BoardBadge.OnSale : BoardBadge.ReservationOpen;
    }
}

/// <summary>Panoda seansın yanında görünen durum (dil anahtarı: "board.badge{Ad}").</summary>
public enum BoardBadge
{
    OnSale,
    ReservationOpen,
    BoxOffice,
    SalesSoon,
    SalesClosed,
    Now,
    Cancelled,
}
