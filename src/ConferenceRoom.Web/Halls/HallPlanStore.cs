using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Halls;

/// <summary>
/// Kroki sürümlerini okur ve kaydeder. Editör "Kaydet"e bastığında gelen veri burada denetlenir ve yeni sürüm olarak eklenir.
///
/// Çakışma kuralı: editör açıldığında hangi sürümü temel aldığını (<c>baseVersion</c>) bilir. O arada başka biri yeni sürüm
/// kaydettiyse kayıt reddedilir; böylece iki kişi aynı krokiyi düzenlerken biri diğerinin işini fark etmeden ezemez.
/// </summary>
public static class HallPlanStore
{
    public enum Outcome { Saved, Unchanged, Conflict, Invalid }

    /// <param name="Version">Kayıttan sonra salonun güncel sürüm numarası.</param>
    /// <param name="ErrorKey">Invalid ise hatanın dil anahtarı.</param>
    public sealed record SaveResult(Outcome Outcome, int Version, int Capacity, string? ErrorKey = null);

    /// <summary>Salonun güncel (en yüksek numaralı) sürüm numarası; hiç kroki kaydedilmemişse 0.</summary>
    public static async Task<int> LatestVersionAsync(AppDbContext db, int hallId, CancellationToken cancellationToken) =>
        await db.HallPlanVersions.Where(v => v.HallId == hallId).MaxAsync(v => (int?)v.Version, cancellationToken) ?? 0;

    public static async Task<SaveResult> SaveAsync(AppDbContext db, Hall hall, string json, int baseVersion, int staffUserId, CancellationToken cancellationToken)
    {
        var latest = await db.HallPlanVersions
            .Where(v => v.HallId == hall.Id)
            .OrderByDescending(v => v.Version)
            .Select(v => new { v.Version, v.PlanJson, v.Capacity })
            .FirstOrDefaultAsync(cancellationToken);
        var latestVersion = latest?.Version ?? 0;

        // ---- Denetimler: başkası yeni sürüm kaydetmiş mi, veri geçerli mi, gerçekten değişmiş mi ----
        if (baseVersion != latestVersion)
            return new SaveResult(Outcome.Conflict, latestVersion, latest?.Capacity ?? 0);

        var plan = HallPlan.Read(json, hall.Name);
        if (!plan.IsValid)
            return new SaveResult(Outcome.Invalid, latestVersion, latest?.Capacity ?? 0, plan.ErrorKey);

        if (latest is not null && latest.PlanJson == plan.Json)
            return new SaveResult(Outcome.Unchanged, latestVersion, latest.Capacity);

        // ---- Yeni sürüm ----
        db.HallPlanVersions.Add(new HallPlanVersion
        {
            HallId = hall.Id,
            Version = latestVersion + 1,
            PlanJson = plan.Json!,
            Capacity = plan.Capacity,
            CreatedByStaffUserId = staffUserId,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Aynı anda iki kayıt: (HallId, Version) benzersiz indeksi ikincisini durdurur.
            db.ChangeTracker.Clear();
            return new SaveResult(Outcome.Conflict, await LatestVersionAsync(db, hall.Id, cancellationToken), 0);
        }
        return new SaveResult(Outcome.Saved, latestVersion + 1, plan.Capacity);
    }
}
