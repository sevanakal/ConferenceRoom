using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim.Salonlar;

/// <summary>
/// Salon ekleme ve düzenleme (/yonetim/salonlar/duzenle/{id?}); id yoksa yeni salon.
/// Yeni salon kaydedilince doğrudan kroki editörü açılır. Düzenlemede salonun kroki sürümleri de listelenir;
/// eski bir sürüm editörde açılıp kaydedilerek geri alınabilir (yeni sürüm olarak).
/// </summary>
public class DuzenleModel(AppDbContext db) : PageModel
{
    public sealed class HallInput
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed record VersionRow(int Version, int Capacity, DateTime CreatedAtUtc, string SavedBy);

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty]
    public HallInput Input { get; set; } = new();

    /// <summary>Kroki sürümleri, yeniden eskiye.</summary>
    public IReadOnlyList<VersionRow> Versions { get; private set; } = [];

    /// <summary>Alan adı ("Name") → hata metninin dil anahtarı.</summary>
    public Dictionary<string, string> Errors { get; } = [];

    [TempData]
    public string? Flash { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Id is null)
            return Page();

        var hall = await FindAsync(cancellationToken);
        if (hall is null)
            return NotFound();

        Input = new HallInput { Name = hall.Name, Description = hall.Description, IsActive = hall.IsActive };
        await LoadVersionsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var hall = Id is null ? null : await FindAsync(cancellationToken);
        if (Id is not null && hall is null)
            return NotFound();

        // ---- Doğrulama ----
        var name = Input.Name?.Trim() ?? "";
        if (name.Length is 0 or > 200)
            Errors["Name"] = "halls.nameRequired";
        else if (await db.Halls.AnyAsync(h => h.OrganizationId == Organization.DefaultId && h.Name == name && h.Id != (Id ?? 0), cancellationToken))
            Errors["Name"] = "halls.nameTaken";
        if (Errors.Count > 0)
        {
            await LoadVersionsAsync(cancellationToken);
            return Page();
        }

        // ---- Kayıt ----
        var isNew = hall is null;
        hall ??= db.Halls.Add(new Hall { OrganizationId = Organization.DefaultId, Name = name }).Entity;
        hall.Name = name;
        var description = Input.Description?.Trim();
        hall.Description = string.IsNullOrEmpty(description) ? null : description[..Math.Min(description.Length, 1000)];
        hall.IsActive = Input.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        if (isNew)
            return Redirect($"/yonetim/salonlar/{hall.Id}/kroki");
        Flash = "halls.saved";
        return Redirect("/yonetim/salonlar");
    }

    // ---- Yardımcılar ----

    private Task<Hall?> FindAsync(CancellationToken cancellationToken) =>
        db.Halls.FirstOrDefaultAsync(h => h.Id == Id && h.OrganizationId == Organization.DefaultId, cancellationToken);

    private async Task LoadVersionsAsync(CancellationToken cancellationToken)
    {
        if (Id is null)
            return;
        Versions = await db.HallPlanVersions
            .Where(v => v.HallId == Id)
            .OrderByDescending(v => v.Version)
            .Select(v => new VersionRow(v.Version, v.Capacity, v.CreatedAtUtc, v.CreatedBy!.FullName))
            .ToListAsync(cancellationToken);
    }
}
