using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Domain.Sales;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Events;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Sales;

/// <summary>
/// Satış raporu (/yonetim/satislar, "sales.view" yetkisi): seans bazında satılabilir koltuk, satılan, rezerve edilen, kalan
/// koltuk ve tahsil edilen / bekleyen ödeme; seansın işlem listesi. Yalnızca okur, hiçbir şeyi değiştirmez.
/// Satılabilir koltuk: seansın krokisinde, etkinliğin koltuk ayarlarında kategorisi olan, protokol olmayan koltuk
/// (satış ekranıyla aynı kural, <see cref="SalesDesk"/>). Kalan = satılabilir koltuklardan bileti olmayanlar.
/// </summary>
public static class SalesReport
{
    /// <summary>Raporun bir satırı (bir seans).</summary>
    public sealed record SessionRow(int SessionId, int EventId, string EventTitle, string HallName, DateTime StartsAtUtc, bool Ended,
        EventStatus Status, int Capacity, int Sold, int Reserved, int Remaining, decimal Received, decimal Pending);

    /// <summary>Seansın bir işlemi (satış ya da rezervasyon).</summary>
    public sealed record OrderRow(int Id, DateTime CreatedAtUtc, OrderKind Kind, OrderChannel Channel, string BuyerName, string? BuyerPhone,
        string? Note, IReadOnlyList<string> Seats, decimal Total, PaymentStatus Payment, string? SellerName);

    /// <summary>
    /// Taslak olmayan etkinliklerin seansları, başlangıca göre. <paramref name="includePast"/> false ise bitmiş seanslar
    /// (bitiş saati ya da başlangıçtan <see cref="BoardQuery.DefaultDuration"/> sonrası geçmiş) atlanır.
    /// </summary>
    public static async Task<List<SessionRow>> SessionsAsync(AppDbContext db, bool includePast, DateTime nowUtc, CancellationToken cancellationToken, int? sessionId = null)
    {
        // ---- Seanslar ----
        var sessions = await (
            from s in db.EventSessions
            join e in db.Events on s.EventId equals e.Id
            where e.OrganizationId == Organization.DefaultId && e.Status != EventStatus.Draft && (sessionId == null || s.Id == sessionId)
            select new { s.Id, s.EventId, e.Title, HallName = e.Hall!.Name, e.Status, s.StartsAtUtc, s.EndsAtUtc, s.HallPlanVersionId })
            .ToListAsync(cancellationToken);
        var listed = sessions
            .Select(s => (s, Ended: nowUtc >= (s.EndsAtUtc ?? s.StartsAtUtc + BoardQuery.DefaultDuration)))
            .Where(x => includePast || !x.Ended || sessionId != null)
            .OrderBy(x => x.s.StartsAtUtc)
            .ToList();
        if (listed.Count == 0)
            return [];

        // ---- Krokiler ve koltuk ayarları: her sürüm ve etkinlik bir kez okunur ----
        var sessionIds = listed.Select(x => x.s.Id).ToList();
        var versionIds = listed.Select(x => x.s.HallPlanVersionId).Distinct().ToList();
        var eventIds = listed.Select(x => x.s.EventId).Distinct().ToList();
        var seatsOfVersion = (await db.HallPlanVersions.Where(v => versionIds.Contains(v.Id)).Select(v => new { v.Id, v.PlanJson }).ToListAsync(cancellationToken))
            .ToDictionary(v => v.Id, v => HallPlan.SeatIds(v.PlanJson).ToList());
        var categoriesOfEvent = (await db.EventSeatMaps.Where(m => eventIds.Contains(m.EventId)).Select(m => new { m.EventId, m.MapJson }).ToListAsync(cancellationToken))
            .ToDictionary(m => m.EventId, m => SeatMap.SeatCategories(m.MapJson));

        // ---- Biletler ve ödemeler ----
        var tickets = await (
            from t in db.Tickets
            join o in db.Orders on t.OrderId equals o.Id
            where sessionIds.Contains(t.EventSessionId)
            select new { t.EventSessionId, t.SeatId, o.Kind })
            .ToListAsync(cancellationToken);
        var payments = await db.Orders.Where(o => sessionIds.Contains(o.EventSessionId))
            .GroupBy(o => new { o.EventSessionId, o.Payment })
            .Select(g => new { g.Key.EventSessionId, g.Key.Payment, Total = g.Sum(o => o.Total) })
            .ToListAsync(cancellationToken);

        return listed.Select(x =>
        {
            var s = x.s;
            var categories = categoriesOfEvent.GetValueOrDefault(s.EventId) ?? new Dictionary<string, SeatCategory>();
            var sellable = seatsOfVersion.GetValueOrDefault(s.HallPlanVersionId, [])
                .Where(id => categories.TryGetValue(id, out var c) && !c.Protocol).ToHashSet(StringComparer.Ordinal);
            var own = tickets.Where(t => t.EventSessionId == s.Id).ToList();
            decimal Sum(PaymentStatus p) => payments.Where(q => q.EventSessionId == s.Id && q.Payment == p).Sum(q => q.Total);
            return new SessionRow(s.Id, s.EventId, s.Title, s.HallName, s.StartsAtUtc, x.Ended, s.Status,
                sellable.Count,
                own.Count(t => t.Kind == OrderKind.Sale),
                own.Count(t => t.Kind == OrderKind.Reservation),
                sellable.Count(id => !own.Any(t => t.SeatId == id)),
                Sum(PaymentStatus.Received),
                Sum(PaymentStatus.Pending));
        }).ToList();
    }

    /// <summary>Seansın işlemleri, yeniden eskiye.</summary>
    public static async Task<List<OrderRow>> OrdersAsync(AppDbContext db, int sessionId, CancellationToken cancellationToken)
    {
        var orders = await db.Orders.Where(o => o.EventSessionId == sessionId && o.OrganizationId == Organization.DefaultId)
            .OrderByDescending(o => o.Id)
            .Select(o => new { o.Id, o.CreatedAtUtc, o.Kind, o.Channel, o.BuyerName, o.BuyerPhone, o.Note, o.Total, o.Payment, Seller = o.SoldBy == null ? null : o.SoldBy.FullName })
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(o => o.Id).ToList();
        var seats = (await db.Tickets.Where(t => orderIds.Contains(t.OrderId)).OrderBy(t => t.Id).Select(t => new { t.OrderId, t.SeatLabel }).ToListAsync(cancellationToken))
            .ToLookup(t => t.OrderId, t => t.SeatLabel);
        return orders.Select(o => new OrderRow(o.Id, o.CreatedAtUtc, o.Kind, o.Channel, o.BuyerName, o.BuyerPhone, o.Note,
            seats[o.Id].ToList(), o.Total, o.Payment, o.Seller)).ToList();
    }
}
