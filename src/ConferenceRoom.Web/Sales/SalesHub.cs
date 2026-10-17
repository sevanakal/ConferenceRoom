using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ConferenceRoom.Web.Sales;

/// <summary>
/// Satış ekranlarının canlı bağlantısı (SignalR, /satis/canli; satış bölümünün çereziyle). Her ekran açtığı seansın grubuna
/// katılır (<see cref="Join"/>). Koltuk seçilince ayrılır (<see cref="Hold"/>), seçim kalkınca bırakılır (<see cref="Release"/>);
/// gruptaki diğer ekranlara anında bildirilir:
///   held(koltuklar)     başka bir ekranda seçildi: "işlemde" görünür, seçilemez
///   released(koltuklar) ayırma kalktı (bırakıldı, süresi doldu ya da ekran kapandı)
///   sold(koltuklar)     satıldı / rezerve edildi (Pages/Satis/Seans.cshtml.cs gönderir)
/// Ayırmalar <see cref="SeatHolds"/> içinde; bağlantı kimliği ayıranın kimliğidir.
/// </summary>
[Authorize(Policy = AuthPolicies.Sales)]
public sealed class SalesHub(SeatHolds holds, AppDbContext db, TimeProvider clock) : Hub
{
    public const string Path = "/satis/canli";

    /// <summary>Bağlantıda saklanan: katılınan seans ve personelin o seansta satabileceği koltuklar.</summary>
    private const string SessionKey = "session", SellableKey = "sellable";

    /// <summary>Gruplar seans başınadır.</summary>
    public static string Group(int sessionId) => $"seans-{sessionId}";

    /// <summary>Seansa katılır; başka ekranlarda ayrılmış koltukları döner. Seans satışa kapalıysa ayırma yapılamaz.</summary>
    public async Task<IReadOnlyList<string>> Join(int sessionId)
    {
        var sellable = await SalesDesk.SellableSeatsAsync(db, Context.User!.UserId(), sessionId, clock.GetUtcNow().UtcDateTime, Context.ConnectionAborted);
        Context.Items[SessionKey] = sessionId;
        Context.Items[SellableKey] = sellable;
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(sessionId));
        return holds.HeldByOthers(sessionId, Context.ConnectionId);
    }

    /// <summary>Koltukları ayırır. Cevap: { held: { koltuk: bitiş (ms, UTC) }, rejected: [başkasında ya da satılamayan] }.</summary>
    public async Task<object> Hold(string[] seatIds)
    {
        if (Context.Items[SessionKey] is not int sessionId || Context.Items[SellableKey] is not IReadOnlySet<string> sellable)
            return new { held = new Dictionary<string, long>(), rejected = seatIds };
        var wanted = seatIds.Take(SalesDesk.MaxSeatsPerOrder).Where(sellable.Contains).ToList();
        var result = holds.TryHold(sessionId, Context.ConnectionId, Context.User!.UserId(), wanted);
        if (result.Held.Count > 0)
            await Clients.OthersInGroup(Group(sessionId)).SendAsync("held", result.Held.Keys);
        return new
        {
            held = result.Held.ToDictionary(h => h.Key, h => new DateTimeOffset(h.Value, TimeSpan.Zero).ToUnixTimeMilliseconds()),
            rejected = seatIds.Except(result.Held.Keys),
        };
    }

    /// <summary>Koltukların ayırmasını bırakır.</summary>
    public async Task Release(string[] seatIds)
    {
        if (Context.Items[SessionKey] is not int sessionId)
            return;
        var released = holds.Release(sessionId, Context.ConnectionId, seatIds);
        if (released.Count > 0)
            await Clients.OthersInGroup(Group(sessionId)).SendAsync("released", released);
    }

    /// <summary>Ekran kapandı ya da bağlantı koptu: ayırmaları kalkar.</summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var (sessionId, seats) in holds.ReleaseHolder(Context.ConnectionId))
            await Clients.Group(Group(sessionId)).SendAsync("released", seats);
        await base.OnDisconnectedAsync(exception);
    }
}
