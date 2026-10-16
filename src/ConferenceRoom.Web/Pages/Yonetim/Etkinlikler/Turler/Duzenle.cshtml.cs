using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim.Etkinlikler.Turler;

/// <summary>
/// Etkinlik türü ekleme ve düzenleme (/yonetim/etkinlikler/turler/duzenle/{id?}).
/// Türün satış türü ve onay kuralı yeni etkinliklere varsayılan olarak gelir; mevcut etkinlikler değişmez.
/// </summary>
public class DuzenleModel(AppDbContext db) : PageModel
{
    public sealed class TypeInput
    {
        public string? Name { get; set; }
        public SaleMode SaleMode { get; set; } = SaleMode.Reservation;
        public bool RequiresApproval { get; set; }
        public bool IsActive { get; set; } = true;
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty]
    public TypeInput Input { get; set; } = new();

    /// <summary>Alan adı → hata metninin dil anahtarı.</summary>
    public Dictionary<string, string> Errors { get; } = [];

    [TempData]
    public string? Flash { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Id is null)
            return Page();
        var type = await FindAsync(cancellationToken);
        if (type is null)
            return NotFound();
        Input = new TypeInput { Name = type.Name, SaleMode = type.DefaultSaleMode, RequiresApproval = type.DefaultRequiresApproval, IsActive = type.IsActive };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var type = Id is null ? null : await FindAsync(cancellationToken);
        if (Id is not null && type is null)
            return NotFound();

        // ---- Doğrulama ----
        var name = Input.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
            Errors["Name"] = "eventTypes.nameRequired";
        else if (await db.EventTypes.AnyAsync(t => t.OrganizationId == Organization.DefaultId && t.Name == name && t.Id != (Id ?? 0), cancellationToken))
            Errors["Name"] = "eventTypes.nameTaken";
        if (Errors.Count > 0)
            return Page();

        // ---- Kayıt ----
        type ??= db.EventTypes.Add(new EventType { OrganizationId = Organization.DefaultId, Name = name }).Entity;
        type.Name = name;
        type.DefaultSaleMode = Enum.IsDefined(Input.SaleMode) ? Input.SaleMode : SaleMode.Reservation;
        type.DefaultRequiresApproval = Input.RequiresApproval;
        type.IsActive = Input.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        Flash = "eventTypes.saved";
        return Redirect("/yonetim/etkinlikler/turler");
    }

    private Task<EventType?> FindAsync(CancellationToken cancellationToken) =>
        db.EventTypes.FirstOrDefaultAsync(t => t.Id == Id && t.OrganizationId == Organization.DefaultId, cancellationToken);
}
