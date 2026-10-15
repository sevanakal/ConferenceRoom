using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim.Kullanicilar;

/// <summary>Yönetim kullanıcıları listesi (/yonetim/kullanicilar). "users.manage" yetkisi ister (Program.cs).</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public sealed record UserRow(int Id, string FullName, string Email, bool IsActive, DateTime? LastLoginAtUtc, IReadOnlyList<string> Roles);

    public IReadOnlyList<UserRow> Users { get; private set; } = [];

    [TempData]
    public string? Flash { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Users = await db.StaffUsers
            .Where(u => u.OrganizationId == Organization.DefaultId)
            .OrderByDescending(u => u.IsActive).ThenBy(u => u.FullName)
            .Select(u => new UserRow(u.Id, u.FullName, u.Email, u.IsActive, u.LastLoginAtUtc,
                u.Roles.Select(r => r.Role!.Name).OrderBy(n => n).ToList()))
            .ToListAsync(cancellationToken);
    }
}
