using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Domain.Sales;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Events;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Sales;

/// <summary>
/// Personelin satabileceği koltuklar (koltuk kapsamı, <see cref="SeatScope"/>): ekran için okuma ve kaydetme,
/// satış sırasında da "bu personel bu koltuğu satabilir mi" sorusunun cevabı (<see cref="AllowedSeatsAsync"/>).
///
/// Kayıtlar salon başınadır (salonun tüm etkinlikleri); bir etkinliğe özel kayıt varsa o etkinlikte salon kaydının yerine geçer.
/// Kayıt denetiminde salonun tüm kroki sürümlerinin koltukları geçerli sayılır (koltuk kimlikleri sürümler arasında korunur).
/// Çakışma kuralı kroki editöründeki gibidir: ekran açıldıktan sonra başkası kaydettiyse kayıt reddedilir.
/// </summary>
public static class SeatScopeStore
{
    public enum Outcome { Saved, Unchanged, Conflict, Invalid }

    /// <param name="Revision">Kayıttan sonra güncel sürüm.</param>
    /// <param name="ErrorKey">Invalid ise hatanın dil anahtarı.</param>
    public sealed record SaveResult(Outcome Outcome, int Revision, string? ErrorKey = null);

    /// <summary>Ekranın açılışı için veri.</summary>
    /// <param name="ScopeJson">Gösterilecek kapsam. Etkinlik ekranında etkinliğe özel kayıt yoksa salonun kaydı.</param>
    /// <param name="Revision">Ekranın kaydettiği kaydın sürümü (henüz kayıt yoksa 0).</param>
    /// <param name="IsEventSpecific">Etkinlik ekranında etkinliğe özel kayıt var mı.</param>
    /// <param name="PlanJson">Gösterilecek kroki: etkinlikte seansların bağlı olduğu en yeni sürüm, salonda son sürüm; yoksa null.</param>
    public sealed record Snapshot(string ScopeJson, int Revision, bool IsEventSpecific, string? PlanJson);

    public static async Task<Snapshot> LoadAsync(AppDbContext db, int hallId, int? eventId, CancellationToken cancellationToken)
    {
        var hallScope = await FindAsync(db, hallId, null, cancellationToken);
        string? planJson;
        if (eventId is { } id)
        {
            var eventScope = await FindAsync(db, hallId, id, cancellationToken);
            planJson = (await SeatMapStore.PlanVersionsAsync(db, id, cancellationToken)).LastOrDefault()?.PlanJson ?? await LatestPlanAsync(db, hallId, cancellationToken);
            return new Snapshot(eventScope?.ScopeJson ?? hallScope?.ScopeJson ?? SeatScopeData.Empty, eventScope?.Revision ?? 0, eventScope is not null, planJson);
        }
        planJson = await LatestPlanAsync(db, hallId, cancellationToken);
        return new Snapshot(hallScope?.ScopeJson ?? SeatScopeData.Empty, hallScope?.Revision ?? 0, false, planJson);
    }

    public static async Task<SaveResult> SaveAsync(AppDbContext db, Hall hall, int? eventId, string json, int baseRevision, int staffUserId, CancellationToken cancellationToken)
    {
        var scope = await FindAsync(db, hall.Id, eventId, cancellationToken);
        var revision = scope?.Revision ?? 0;

        // ---- Denetimler: başkası kaydetmiş mi, veri geçerli mi, gerçekten değişmiş mi ----
        if (baseRevision != revision)
            return new SaveResult(Outcome.Conflict, revision);
        var plans = await db.HallPlanVersions.Where(v => v.HallId == hall.Id).Select(v => v.PlanJson).ToListAsync(cancellationToken);
        var seatIds = plans.SelectMany(HallPlan.SeatIds).ToHashSet(StringComparer.Ordinal);
        var staffIds = (await db.StaffUsers.Where(u => u.OrganizationId == hall.OrganizationId).Select(u => u.Id).ToListAsync(cancellationToken)).ToHashSet();
        var result = SeatScopeData.Read(json, seatIds, staffIds);
        if (!result.IsValid)
            return new SaveResult(Outcome.Invalid, revision, result.ErrorKey);
        if (scope?.ScopeJson == result.Json)
            return new SaveResult(Outcome.Unchanged, revision);

        // ---- Kayıt ----
        if (scope is null)
        {
            scope = new SeatScope { OrganizationId = hall.OrganizationId, HallId = hall.Id, EventId = eventId, ScopeJson = result.Json! };
            db.SeatScopes.Add(scope);
        }
        scope.ScopeJson = result.Json!;
        scope.Revision = revision + 1;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        scope.UpdatedByStaffUserId = staffUserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Aynı anda iki kayıt: ilk kayıtta (HallId, EventKey) benzersiz indeksi, sonrakilerde Revision eşzamanlılık denetimi durdurur.
            db.ChangeTracker.Clear();
            var latest = await FindAsync(db, hall.Id, eventId, cancellationToken);
            return new SaveResult(Outcome.Conflict, latest?.Revision ?? 0);
        }
        return new SaveResult(Outcome.Saved, scope.Revision);
    }

    /// <summary>Etkinliğe özel kaydı siler; etkinlik yeniden salonun kaydını kullanır.</summary>
    public static async Task ResetEventAsync(AppDbContext db, int hallId, int eventId, CancellationToken cancellationToken)
    {
        var scope = await FindAsync(db, hallId, eventId, cancellationToken);
        if (scope is null)
            return;
        db.SeatScopes.Remove(scope);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Personelin bu etkinlikte satabileceği koltuklar; null ise tüm koltuklar (koltuk kısıtlaması yok).
    /// Kısıtlı personel için: etkinliğe özel kayıt varsa oradaki, yoksa salon kaydındaki koltukları; hiçbirinde yoksa boş küme.
    /// Satış ve rezervasyon her işlemde bunu sunucuda denetler (<see cref="Allows"/>).
    /// </summary>
    public static async Task<IReadOnlySet<string>?> AllowedSeatsAsync(AppDbContext db, int staffUserId, Event ev, CancellationToken cancellationToken)
    {
        var restricted = await db.StaffUsers.Where(u => u.Id == staffUserId).Select(u => (bool?)u.SeatsRestricted).FirstOrDefaultAsync(cancellationToken);
        if (restricted is false)
            return null;
        if (restricted is null)
            return new HashSet<string>();
        var scope = await FindAsync(db, ev.HallId, ev.Id, cancellationToken) ?? await FindAsync(db, ev.HallId, null, cancellationToken);
        return scope is null ? new HashSet<string>() : SeatScopeData.SeatsOf(scope.ScopeJson, staffUserId);
    }

    /// <summary>İstenen koltukların hepsi izin verilenler arasında mı (null: tüm koltuklar serbest).</summary>
    public static bool Allows(IReadOnlySet<string>? allowed, IEnumerable<string> seatIds) =>
        allowed is null || seatIds.All(allowed.Contains);

    /// <summary>Kapsam ekranında listelenecek personel: satış yetkisi olan aktif kullanıcılar, ad sırasıyla.</summary>
    public static Task<List<StaffRow>> SellersAsync(AppDbContext db, int organizationId, CancellationToken cancellationToken) => db.StaffUsers
        .Where(u => u.OrganizationId == organizationId && u.IsActive
            && u.Roles.Any(r => db.RolePermissions.Any(p => p.RoleId == r.RoleId && p.Permission == Permissions.SalesSell)))
        .OrderBy(u => u.FullName)
        .Select(u => new StaffRow(u.Id, u.FullName, u.SeatsRestricted))
        .ToListAsync(cancellationToken);

    public sealed record StaffRow(int Id, string FullName, bool SeatsRestricted);

    // ---- Yardımcılar ----

    private static Task<SeatScope?> FindAsync(AppDbContext db, int hallId, int? eventId, CancellationToken cancellationToken) =>
        db.SeatScopes.FirstOrDefaultAsync(s => s.HallId == hallId && s.EventId == eventId, cancellationToken);

    private static Task<string?> LatestPlanAsync(AppDbContext db, int hallId, CancellationToken cancellationToken) =>
        db.HallPlanVersions.Where(v => v.HallId == hallId).OrderByDescending(v => v.Version).Select(v => v.PlanJson).FirstOrDefaultAsync(cancellationToken);
}
