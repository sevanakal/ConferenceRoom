using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim.Roller;

/// <summary>
/// Rol ekleme ve düzenleme (/yonetim/roller/duzenle/{id?}); id yoksa yeni rol.
/// Kayıt ve silme bir veritabanı işlemi (transaction) içinde yapılır: sonunda kullanıcıları
/// yönetebilen aktif kimse kalmıyorsa işlem geri alınır (panelin kilitlenmemesi için).
/// </summary>
public class DuzenleModel(AppDbContext db) : PageModel
{
    public sealed class RoleInput
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<string> Permissions { get; set; } = [];
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty]
    public RoleInput Input { get; set; } = new();

    public bool IsSystem { get; private set; }

    public int UserCount { get; private set; }

    /// <summary>Alan adı ("Name") ya da "" (formun geneli) → hata metninin dil anahtarı.</summary>
    public Dictionary<string, string> Errors { get; } = [];

    [TempData]
    public string? Flash { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Id is null)
            return Page();

        var role = await FindAsync(cancellationToken);
        if (role is null)
            return NotFound();

        Input = InputFrom(role);
        await LoadInfoAsync(role, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var role = Id is null ? null : await FindAsync(cancellationToken);
        if (Id is not null && role is null)
            return NotFound();

        // ---- Doğrulama ----
        var name = Input.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
            Errors["Name"] = "roles.nameRequired";
        else if (await db.Roles.AnyAsync(r => r.OrganizationId == Organization.DefaultId && r.Name == name && r.Id != (Id ?? 0), cancellationToken))
            Errors["Name"] = "roles.nameTaken";
        if (Errors.Count > 0)
            return await ShowAsync(role, cancellationToken);

        // ---- Kayıt ----
        role ??= db.Roles.Add(new Role { OrganizationId = Organization.DefaultId, Name = name }).Entity;
        role.Name = name;
        role.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim()[..Math.Min(Input.Description.Trim().Length, 500)];
        var selected = Permissions.All.Where(Input.Permissions.Contains).ToHashSet();   // bilinmeyen kodlar yok sayılır
        role.Permissions = role.Permissions.Where(p => selected.Contains(p.Permission)).ToList();
        foreach (var permission in selected.Where(p => role.Permissions.All(x => x.Permission != p)))
            role.Permissions.Add(new RolePermission { Permission = permission });

        if (!await SaveKeepingAManagerAsync(cancellationToken))
            return await ShowAsync(role, cancellationToken);

        Flash = "roles.saved";
        return Redirect("/yonetim/roller");
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var role = Id is null ? null : await FindAsync(cancellationToken);
        if (role is null)
            return NotFound();
        Input = InputFrom(role);   // silinemezse form rolün mevcut hâliyle yeniden gösterilir
        if (role.IsSystem)
        {
            Errors[""] = "roles.systemNoDelete";
            return await ShowAsync(role, cancellationToken);
        }

        db.Roles.Remove(role);   // kullanıcı-rol bağları ve rol yetkileri veritabanında birlikte silinir (cascade)
        if (!await SaveKeepingAManagerAsync(cancellationToken))
            return await ShowAsync(role, cancellationToken);

        Flash = "roles.deleted";
        return Redirect("/yonetim/roller");
    }

    // ---- Yardımcılar ----

    private static RoleInput InputFrom(Role role) =>
        new() { Name = role.Name, Description = role.Description, Permissions = role.Permissions.Select(p => p.Permission).ToList() };

    private Task<Role?> FindAsync(CancellationToken cancellationToken) => db.Roles
        .Include(r => r.Permissions)
        .FirstOrDefaultAsync(r => r.Id == Id && r.OrganizationId == Organization.DefaultId, cancellationToken);

    private async Task LoadInfoAsync(Role? role, CancellationToken cancellationToken)
    {
        IsSystem = role?.IsSystem ?? false;
        UserCount = role is null || role.Id == 0 ? 0 : await db.StaffUserRoles.CountAsync(ur => ur.RoleId == role.Id, cancellationToken);
    }

    private async Task<IActionResult> ShowAsync(Role? role, CancellationToken cancellationToken)
    {
        await LoadInfoAsync(role, cancellationToken);
        return Page();
    }

    /// <summary>Değişiklikleri kaydeder; ardından yönetici kalmadıysa geri alır ve false döner.</summary>
    private async Task<bool> SaveKeepingAManagerAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (await StaffAccounts.CountManagersAsync(db, cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            Errors[""] = "admin.lastManager";
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
