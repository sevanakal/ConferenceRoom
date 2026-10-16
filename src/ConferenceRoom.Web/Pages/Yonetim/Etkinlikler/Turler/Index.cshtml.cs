using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim.Etkinlikler.Turler;

/// <summary>Etkinlik türleri listesi (/yonetim/etkinlikler/turler). "events.manage" yetkisi ister (Program.cs: Etkinlikler klasörü).</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public sealed record TypeRow(int Id, string Name, SaleMode SaleMode, bool RequiresApproval, bool IsActive, int EventCount);

    public IReadOnlyList<TypeRow> Types { get; private set; } = [];

    [TempData]
    public string? Flash { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Types = await db.EventTypes
            .Where(t => t.OrganizationId == Organization.DefaultId)
            .OrderByDescending(t => t.IsActive).ThenBy(t => t.Name)
            .Select(t => new TypeRow(t.Id, t.Name, t.DefaultSaleMode, t.DefaultRequiresApproval, t.IsActive, db.Events.Count(e => e.EventTypeId == t.Id)))
            .ToListAsync(cancellationToken);
    }
}
