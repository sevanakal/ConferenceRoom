using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim.Salonlar;

/// <summary>Salon listesi (/yonetim/salonlar): her salonun güncel kroki sürümü ve kapasitesi. "halls.manage" yetkisi ister (Program.cs).</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    /// <param name="Version">Güncel kroki sürümü; kroki hiç kaydedilmemişse null.</param>
    public sealed record HallRow(int Id, string Name, string? Description, bool IsActive, int? Version, int Capacity, DateTime? SavedAtUtc, string? SavedBy);

    public IReadOnlyList<HallRow> Halls { get; private set; } = [];

    [TempData]
    public string? Flash { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var halls = await db.Halls
            .Where(h => h.OrganizationId == Organization.DefaultId)
            .OrderByDescending(h => h.IsActive).ThenBy(h => h.Name)
            .Select(h => new { h.Id, h.Name, h.Description, h.IsActive })
            .ToListAsync(cancellationToken);

        // Her salonun en yüksek numaralı sürümü (kroki verisinin kendisi okunmaz, yalnızca özet alanlar).
        var hallIds = halls.Select(h => h.Id).ToList();
        var latest = await db.HallPlanVersions
            .Where(v => hallIds.Contains(v.HallId) && v.Version == db.HallPlanVersions.Where(x => x.HallId == v.HallId).Max(x => x.Version))
            .Select(v => new { v.HallId, v.Version, v.Capacity, v.CreatedAtUtc, SavedBy = v.CreatedBy!.FullName })
            .ToDictionaryAsync(v => v.HallId, cancellationToken);

        Halls = halls.Select(h =>
        {
            var v = latest.GetValueOrDefault(h.Id);
            return new HallRow(h.Id, h.Name, h.Description, h.IsActive, v?.Version, v?.Capacity ?? 0, v?.CreatedAtUtc, v?.SavedBy);
        }).ToList();
    }
}
