using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Sales;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Sales;

/// <summary>
/// Satış bildirimleri: bir satış ya da rezervasyon kaydedilince "Satış bildirimlerini al" yetkisi (<see cref="Permissions.SalesNotify"/>)
/// olan her aktif yönetim kullanıcısına bir <see cref="Notification"/> kaydı açar ve paneli açık olanlara canlı gönderir
/// (<see cref="NotificationHub"/>, "notify"). Bildirimin metni kayıtta değil, gösterilirken üretilir (<see cref="Item"/>).
/// </summary>
public static class SalesNotifier
{
    /// <summary>Panelde gösterilen bir bildirim (ve canlı gönderilen veri).</summary>
    public sealed record Item(int Id, DateTime CreatedAtUtc, bool IsRead, int OrderId, OrderKind Kind, int SeatCount, decimal Total,
        string BuyerName, string EventTitle, DateTime SessionStartsAtUtc, string? SellerName, int SessionId);

    public static async Task NotifyAsync(AppDbContext db, IHubContext<NotificationHub> hub, Order order, CancellationToken cancellationToken)
    {
        // ---- Alıcılar: yetkisi olan aktif kullanıcılar ----
        var recipients = await db.StaffUsers
            .Where(u => u.OrganizationId == order.OrganizationId && u.IsActive
                && u.Roles.Any(r => db.RolePermissions.Any(p => p.RoleId == r.RoleId && p.Permission == Permissions.SalesNotify)))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        if (recipients.Count == 0)
            return;

        var notifications = recipients.Select(id => new Notification { OrganizationId = order.OrganizationId, StaffUserId = id, OrderId = order.Id, CreatedAtUtc = order.CreatedAtUtc }).ToList();
        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync(cancellationToken);

        // ---- Canlı gönderim: her kullanıcıya kendi bildirim kaydıyla ----
        var item = await Items(db.Notifications.Where(n => n.Id == notifications[0].Id)).SingleAsync(cancellationToken);
        foreach (var n in notifications)
            await hub.Clients.User(n.StaffUserId.ToString(System.Globalization.CultureInfo.InvariantCulture)).SendAsync("notify", item with { Id = n.Id }, cancellationToken);
    }

    /// <summary>Kullanıcının son bildirimleri, yeniden eskiye.</summary>
    public static Task<List<Item>> LatestAsync(AppDbContext db, int staffUserId, int count, CancellationToken cancellationToken) =>
        Items(db.Notifications.Where(n => n.StaffUserId == staffUserId).OrderByDescending(n => n.Id).Take(count)).ToListAsync(cancellationToken);

    public static Task<int> UnreadCountAsync(AppDbContext db, int staffUserId, CancellationToken cancellationToken) =>
        db.Notifications.CountAsync(n => n.StaffUserId == staffUserId && n.ReadAtUtc == null, cancellationToken);

    public static Task MarkAllReadAsync(AppDbContext db, int staffUserId, DateTime nowUtc, CancellationToken cancellationToken) =>
        db.Notifications.Where(n => n.StaffUserId == staffUserId && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, nowUtc), cancellationToken);

    // ---- Yardımcılar ----

    /// <summary>Bildirim kayıtlarını işlem, etkinlik, seans ve satan personel bilgisiyle gösterilecek hâle getirir.</summary>
    private static IQueryable<Item> Items(IQueryable<Notification> notifications) =>
        notifications.Select(n => new Item(n.Id, n.CreatedAtUtc, n.ReadAtUtc != null, n.OrderId, n.Order!.Kind, n.Order.Tickets.Count, n.Order.Total,
            n.Order.BuyerName, n.Order.Event!.Title, n.Order.EventSession!.StartsAtUtc, n.Order.SoldBy == null ? null : n.Order.SoldBy.FullName,
            n.Order.EventSessionId));
}

/// <summary>
/// Yönetim panelinin canlı bağlantısı (SignalR, /yonetim/canli; yönetim çereziyle). Yalnızca bildirim göndermek içindir:
/// sunucu "notify" ile yeni bildirimi kullanıcıya (kullanıcı kimliğine göre) yollar; istemci wwwroot/js/bildirimler.js.
/// </summary>
[Authorize(Policy = AuthPolicies.Staff)]
public sealed class NotificationHub : Hub
{
    public const string Path = "/yonetim/canli";
}
