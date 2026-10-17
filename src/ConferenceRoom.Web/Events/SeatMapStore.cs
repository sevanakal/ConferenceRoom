using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Events;

/// <summary>
/// Etkinlik koltuk ayarlarını (kategoriler, fiyatlar, koltuk atamaları, online koltuklar) okur ve kaydeder.
/// Ekranda gösterilen kroki, etkinliğin seanslarının bağlı olduğu en yeni kroki sürümüdür; koltuk kimlikleri sürümler
/// arasında korunduğu için ayarlar tüm seanslara uygulanır. Duvar, kapı, sahne gibi yapı öğeleri ise salonun son
/// sürümünden gelir (<see cref="WithLatestStructureAsync"/>), çünkü bunlar satılmaz ve sonradan çizilenler de görünmeli. Kayıt denetiminde seansların bağlı olduğu tüm sürümlerin
/// koltukları geçerli sayılır.
///
/// Çakışma kuralı (kroki editöründeki gibi): ekran açıldığında hangi kayda (<c>Revision</c>) dayandığını bilir;
/// o arada başkası kaydettiyse kayıt reddedilir.
/// </summary>
public static class SeatMapStore
{
    public enum Outcome { Saved, Unchanged, Conflict, Invalid }

    /// <param name="Revision">Kayıttan sonra etkinliğin güncel ayar sürümü.</param>
    /// <param name="ErrorKey">Invalid ise hatanın dil anahtarı.</param>
    public sealed record SaveResult(Outcome Outcome, int Revision, string? ErrorKey = null);

    /// <summary>Ekranın açılışı için: ayarlar, dayanılan sürüm ve gösterilecek kroki.</summary>
    /// <param name="PlanJson">Seansların bağlı olduğu en yeni kroki sürümü (yapı öğeleri salonun son sürümünden); seans yoksa null.</param>
    /// <param name="PlanVersions">Seansların bağlı olduğu farklı kroki sürüm numaraları (birden çoksa ekranda not çıkar).</param>
    public sealed record Snapshot(string MapJson, int Revision, string? PlanJson, IReadOnlyList<int> PlanVersions);

    public static async Task<Snapshot> LoadAsync(AppDbContext db, int eventId, CancellationToken cancellationToken)
    {
        var map = await db.EventSeatMaps.Where(m => m.EventId == eventId)
            .Select(m => new { m.MapJson, m.Revision })
            .FirstOrDefaultAsync(cancellationToken);
        var versions = await PlanVersionsAsync(db, eventId, cancellationToken);
        var planJson = versions.Count == 0 ? null : await WithLatestStructureAsync(db, versions[^1], cancellationToken);
        return new Snapshot(map?.MapJson ?? SeatMap.Empty, map?.Revision ?? 0, planJson, versions.Select(v => v.Version).ToList());
    }

    public static async Task<SaveResult> SaveAsync(AppDbContext db, Event ev, string json, int baseRevision, int staffUserId, CancellationToken cancellationToken)
    {
        var map = await db.EventSeatMaps.FirstOrDefaultAsync(m => m.EventId == ev.Id, cancellationToken);
        var revision = map?.Revision ?? 0;

        // ---- Denetimler: başkası kaydetmiş mi, seans var mı, veri geçerli mi, gerçekten değişmiş mi ----
        if (baseRevision != revision)
            return new SaveResult(Outcome.Conflict, revision);
        var versions = await PlanVersionsAsync(db, ev.Id, cancellationToken);
        if (versions.Count == 0)
            return new SaveResult(Outcome.Invalid, revision, "seatMap.noSessions");
        var seatIds = versions.SelectMany(v => HallPlan.SeatIds(v.PlanJson)).ToHashSet(StringComparer.Ordinal);
        var result = SeatMap.Read(json, seatIds);
        if (!result.IsValid)
            return new SaveResult(Outcome.Invalid, revision, result.ErrorKey);
        if (map?.MapJson == result.Json)
            return new SaveResult(Outcome.Unchanged, revision);

        // ---- Kayıt ----
        if (map is null)
        {
            map = new EventSeatMap { OrganizationId = ev.OrganizationId, EventId = ev.Id, MapJson = result.Json! };
            db.EventSeatMaps.Add(map);
        }
        map.MapJson = result.Json!;
        map.AssignedSeats = result.AssignedSeats;
        map.OnlineSeats = result.OnlineSeats;
        map.Revision = revision + 1;
        map.UpdatedAtUtc = DateTime.UtcNow;
        map.UpdatedByStaffUserId = staffUserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Aynı anda iki kayıt: ilk kayıtta EventId benzersiz indeksi, sonrakilerde Revision eşzamanlılık denetimi durdurur.
            db.ChangeTracker.Clear();
            var latest = await db.EventSeatMaps.Where(m => m.EventId == ev.Id).Select(m => m.Revision).FirstOrDefaultAsync(cancellationToken);
            return new SaveResult(Outcome.Conflict, latest);
        }
        return new SaveResult(Outcome.Saved, map.Revision);
    }

    /// <summary>Etkinliğin seanslarının bağlı olduğu kroki sürümleri, eskiden yeniye (satış yetkileri ekranı da kullanır).</summary>
    internal static async Task<List<HallPlanVersion>> PlanVersionsAsync(AppDbContext db, int eventId, CancellationToken cancellationToken)
    {
        var ids = db.EventSessions.Where(s => s.EventId == eventId).Select(s => s.HallPlanVersionId);
        return await db.HallPlanVersions.Where(v => ids.Contains(v.Id)).OrderBy(v => v.Version).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Bağlı kroki sürümünün koltukları ile salonun son sürümünün yapı öğeleri (duvar, kapı, sahne...). Bağlı sürüm zaten
    /// en yeniyse olduğu gibi döner (satış yetkileri ekranı da kullanır).
    /// </summary>
    internal static async Task<string> WithLatestStructureAsync(AppDbContext db, HallPlanVersion bound, CancellationToken cancellationToken)
    {
        var latest = await db.HallPlanVersions.Where(v => v.HallId == bound.HallId && v.Version > bound.Version)
            .OrderByDescending(v => v.Version).Select(v => v.PlanJson).FirstOrDefaultAsync(cancellationToken);
        return latest is null ? bound.PlanJson : HallPlan.WithStructureFrom(bound.PlanJson, latest);
    }
}
