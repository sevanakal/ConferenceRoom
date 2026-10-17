using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Domain.Sales;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Events;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Sales;

/// <summary>
/// Personel satış ekranının (/satis/seans/{id}) sunucu tarafı: ekranın açılış verisi (<see cref="LoadAsync"/>) ve
/// satış/rezervasyon kaydı (<see cref="SellAsync"/>). Ekran tarayıcıda çalıştığı için her kural burada yeniden denetlenir:
///   - seans satışa açık mı (panodaki durumla aynı kural, <see cref="IsOpen"/>; seans bittiyse kapalı),
///   - koltuklar seansın krokisinde mi, kategorisi var mı, protokol değil mi,
///   - personel bu koltukları satabilir mi (koltuk kapsamı, <see cref="SeatScopeStore.AllowedSeatsAsync"/>),
///   - koltuk başka bir ekranda ayrılmış mı (3 dakikalık ayırma, <see cref="SeatHolds"/>),
///   - koltuk daha önce satılmış mı (aynı anda iki satışta veritabanının benzersiz indeksi ikinciyi durdurur).
/// Fiyat istemciden alınmaz, etkinliğin koltuk ayarlarındaki kategoriden gelir. Toplam 0 ise ödeme sorulmaz.
/// İşlemin türü (satış / rezervasyon) etkinliğin satış türüdür.
/// </summary>
public static class SalesDesk
{
    /// <summary>Tek işlemde satılabilecek en fazla koltuk.</summary>
    public const int MaxSeatsPerOrder = 100;

    /// <summary>
    /// Personel bu durumdaki seansa satış yapabilir mi: satışta, rezervasyon açık, yalnızca gişede ya da şu anda (başladı,
    /// bitmedi). Satışı henüz açılmamış, kapanmış ya da iptal edilmiş seansa satış yapılmaz.
    /// </summary>
    public static bool IsOpen(BoardBadge badge) =>
        badge is BoardBadge.OnSale or BoardBadge.ReservationOpen or BoardBadge.BoxOffice or BoardBadge.Now;

    /// <summary>Ekranın açılış verisi.</summary>
    /// <param name="PlanJson">Seansın krokisi (koltuklar bağlı sürümden, yapı öğeleri salonun son sürümünden).</param>
    /// <param name="MapJson">Etkinliğin koltuk ayarları (kategoriler, fiyatlar).</param>
    /// <param name="Sold">Bu seansta satılmış/rezerve edilmiş koltuklar.</param>
    /// <param name="Allowed">Personelin satabileceği koltuklar; null ise tümü.</param>
    public sealed record Snapshot(Session Session, string PlanJson, string MapJson, IReadOnlyCollection<string> Sold, IReadOnlySet<string>? Allowed);

    /// <summary>Seansın ekranda gösterilen bilgileri.</summary>
    public sealed record Session(int Id, int EventId, string Title, string HallName, DateTime StartsAtUtc, OrderKind Kind, BoardBadge Badge, bool IsOpen);

    /// <summary>
    /// Ekrandan gelen satış isteği. Paid: toplam 0'dan büyükse zorunlu (ödeme alındı mı).
    /// Holder: ekranın canlı bağlantı kimliği; koltukları bu bağlantı ayırdıysa satış onlara izin verir.
    /// </summary>
    public sealed record SellRequest(IReadOnlyList<string> Seats, string? BuyerName, string? BuyerPhone, string? Note, bool? Paid, string? Holder = null);

    public enum Outcome { Sold, Invalid, Taken, Held, Closed }

    /// <param name="ErrorKey">Invalid ise hatanın dil anahtarı.</param>
    /// <param name="Taken">Taken ise başkasının sattığı, Held ise başka ekranda ayrılmış koltukların okunur adları (ör. "A-12").</param>
    /// <param name="Order">Sold ise kaydedilen işlem.</param>
    public sealed record SellResult(Outcome Outcome, string? ErrorKey = null, IReadOnlyCollection<string>? Taken = null, Order? Order = null);

    public static async Task<Snapshot?> LoadAsync(AppDbContext db, int staffUserId, int sessionId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var found = await FindAsync(db, sessionId, nowUtc, cancellationToken);
        if (found is null)
            return null;
        var (session, ev, version) = found.Value;
        var map = await db.EventSeatMaps.Where(m => m.EventId == ev.Id).Select(m => m.MapJson).FirstOrDefaultAsync(cancellationToken);
        return new Snapshot(
            session,
            await SeatMapStore.WithLatestStructureAsync(db, version, cancellationToken),
            map ?? SeatMap.Empty,
            await SoldSeatsAsync(db, sessionId, cancellationToken),
            await SeatScopeStore.AllowedSeatsAsync(db, staffUserId, ev, cancellationToken));
    }

    /// <summary>
    /// Personelin bu seansta satabileceği koltuklar (krokide, kategorili, protokol değil, kapsamında); seans satışa kapalıysa
    /// ya da bulunamazsa null. Canlı bağlantı (<see cref="SalesHub"/>) yalnızca bunları ayırır.
    /// </summary>
    public static async Task<IReadOnlySet<string>?> SellableSeatsAsync(AppDbContext db, int staffUserId, int sessionId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var found = await FindAsync(db, sessionId, nowUtc, cancellationToken);
        if (found is null || !found.Value.Session.IsOpen)
            return null;
        var (_, ev, version) = found.Value;
        var mapJson = await db.EventSeatMaps.Where(m => m.EventId == ev.Id).Select(m => m.MapJson).FirstOrDefaultAsync(cancellationToken);
        var categories = SeatMap.SeatCategories(mapJson ?? SeatMap.Empty);
        var allowed = await SeatScopeStore.AllowedSeatsAsync(db, staffUserId, ev, cancellationToken);
        return HallPlan.SeatIds(version.PlanJson)
            .Where(id => categories.TryGetValue(id, out var c) && !c.Protocol && (allowed is null || allowed.Contains(id)))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Seansta satılmış/rezerve edilmiş koltuk kimlikleri.</summary>
    public static async Task<IReadOnlyCollection<string>> SoldSeatsAsync(AppDbContext db, int sessionId, CancellationToken cancellationToken) =>
        await db.Tickets.Where(t => t.EventSessionId == sessionId).Select(t => t.SeatId).ToListAsync(cancellationToken);

    public static async Task<SellResult> SellAsync(AppDbContext db, SeatHolds holds, int staffUserId, int sessionId, SellRequest request, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // ---- Seans satışa açık mı ----
        var found = await FindAsync(db, sessionId, nowUtc, cancellationToken);
        if (found is null || !found.Value.Session.IsOpen)
            return new SellResult(Outcome.Closed);
        var (session, ev, version) = found.Value;

        // ---- Alıcı bilgileri: ad zorunlu ----
        var buyerName = request.BuyerName?.Trim() ?? "";
        var phone = Optional(request.BuyerPhone);
        var note = Optional(request.Note);
        if (buyerName.Length == 0)
            return new SellResult(Outcome.Invalid, "sales.errorBuyerName");
        if (buyerName.Length > Order.MaxBuyerName || phone?.Length > Order.MaxBuyerPhone || note?.Length > Order.MaxNote)
            return new SellResult(Outcome.Invalid, "sales.errorTooLong");

        // ---- Koltuklar: krokide, kategorili, protokol değil, personelin kapsamında ----
        var seatIds = (request.Seats ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (seatIds.Count == 0)
            return new SellResult(Outcome.Invalid, "sales.errorNoSeats");
        if (seatIds.Count > MaxSeatsPerOrder)
            return new SellResult(Outcome.Invalid, "sales.errorTooManySeats");
        var labels = HallPlan.SeatLabels(version.PlanJson);
        var mapJson = await db.EventSeatMaps.Where(m => m.EventId == ev.Id).Select(m => m.MapJson).FirstOrDefaultAsync(cancellationToken);
        var categories = SeatMap.SeatCategories(mapJson ?? SeatMap.Empty);
        foreach (var seatId in seatIds)
        {
            if (seatId.Length > Ticket.MaxSeatId || !labels.ContainsKey(seatId))
                return new SellResult(Outcome.Invalid, "sales.errorUnknownSeat");
            if (!categories.TryGetValue(seatId, out var category) || category.Protocol)
                return new SellResult(Outcome.Invalid, "sales.errorNotForSale");
        }
        var allowed = await SeatScopeStore.AllowedSeatsAsync(db, staffUserId, ev, cancellationToken);
        if (!SeatScopeStore.Allows(allowed, seatIds))
            return new SellResult(Outcome.Invalid, "sales.errorNotInScope");

        // ---- Ödeme: toplam 0'dan büyükse alındı/alınmadı seçilmiş olmalı ----
        var total = seatIds.Sum(id => categories[id].Price);
        PaymentStatus payment;
        if (total == 0)
            payment = PaymentStatus.NotRequired;
        else if (request.Paid is { } paid)
            payment = paid ? PaymentStatus.Received : PaymentStatus.Pending;
        else
            return new SellResult(Outcome.Invalid, "sales.errorPayment");

        // ---- Başka bir ekranda ayrılmış koltuk var mı ----
        var blocked = holds.BlockedFor(sessionId, request.Holder, staffUserId, seatIds);
        if (blocked.Count > 0)
            return new SellResult(Outcome.Held, Taken: blocked.Select(id => labels[id]).ToList());

        // ---- Daha önce satılmış koltuk var mı ----
        var taken = await TakenAsync(db, sessionId, seatIds, cancellationToken);
        if (taken.Count > 0)
            return new SellResult(Outcome.Taken, Taken: taken);

        // ---- Kayıt ----
        var order = new Order
        {
            OrganizationId = ev.OrganizationId,
            EventId = ev.Id,
            EventSessionId = sessionId,
            Kind = session.Kind,
            Channel = OrderChannel.Staff,
            BuyerName = buyerName,
            BuyerPhone = phone,
            Note = note,
            Total = total,
            Payment = payment,
            SoldByStaffUserId = staffUserId,
            CreatedAtUtc = nowUtc,
            Tickets = seatIds.Select(id => new Ticket
            {
                OrganizationId = ev.OrganizationId,
                EventSessionId = sessionId,
                SeatId = id,
                SeatLabel = Clip(labels[id], Ticket.MaxSeatLabel),
                CategoryName = categories[id].Name,
                Price = categories[id].Price,
                CreatedAtUtc = nowUtc,
            }).ToList(),
        };
        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Aynı anda başka bir satış aynı koltuğu aldı: (EventSessionId, SeatId) benzersiz indeksi bu kaydı durdurdu.
            db.ChangeTracker.Clear();
            return new SellResult(Outcome.Taken, Taken: await TakenAsync(db, sessionId, seatIds, cancellationToken));
        }
        holds.Clear(sessionId, seatIds);
        return new SellResult(Outcome.Sold, Order: order);
    }

    // ---- Yardımcılar ----

    /// <summary>Seans, etkinliği ve bağlı kroki sürümü; taslak etkinliğin seansı ya da başka kurumun seansı bulunmaz.</summary>
    private static async Task<(Session Session, Event Event, HallPlanVersion Version)?> FindAsync(AppDbContext db, int sessionId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var s = await db.EventSessions.Include(x => x.HallPlanVersion).FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        var e = s is null ? null : await db.Events.Include(x => x.Hall)
            .FirstOrDefaultAsync(x => x.Id == s.EventId && x.OrganizationId == Organization.DefaultId && x.Status != EventStatus.Draft, cancellationToken);
        if (s is null || e is null)
            return null;
        var badge = BoardQuery.BadgeOf(e.Status, e.SaleMode, e.OnlineEnabled, e.SalesStartUtc, e.SalesEndUtc, s.StartsAtUtc, nowUtc);
        var endsAt = s.EndsAtUtc ?? s.StartsAtUtc + BoardQuery.DefaultDuration;
        var session = new Session(s.Id, e.Id, e.Title, e.Hall!.Name, s.StartsAtUtc,
            e.SaleMode == SaleMode.Sale ? OrderKind.Sale : OrderKind.Reservation, badge, IsOpen(badge) && nowUtc < endsAt);
        return (session, e, s.HallPlanVersion!);
    }

    private static async Task<List<string>> TakenAsync(AppDbContext db, int sessionId, List<string> seatIds, CancellationToken cancellationToken) =>
        await db.Tickets.Where(t => t.EventSessionId == sessionId && seatIds.Contains(t.SeatId)).Select(t => t.SeatLabel).ToListAsync(cancellationToken);

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
}
