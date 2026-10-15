using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim.Roller;

/// <summary>Rol listesi (/yonetim/roller): her rolün yetkileri ve kaç kullanıcıda olduğu. "users.manage" yetkisi ister (Program.cs).</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public sealed record RoleRow(int Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int UserCount);

    public IReadOnlyList<RoleRow> Roles { get; private set; } = [];

    /// <summary>Düzenleme sayfasından dönüldüğünde gösterilecek bilginin dil anahtarı (ör. "roles.saved").</summary>
    [TempData]
    public string? Flash { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Roles = await db.Roles
            .Where(r => r.OrganizationId == Organization.DefaultId)
            .OrderBy(r => r.Name)
            .Select(r => new RoleRow(r.Id, r.Name, r.Description, r.IsSystem,
                r.Permissions.Select(p => p.Permission).ToList(),
                db.StaffUserRoles.Count(ur => ur.RoleId == r.Id)))
            .ToListAsync(cancellationToken);
    }
}
