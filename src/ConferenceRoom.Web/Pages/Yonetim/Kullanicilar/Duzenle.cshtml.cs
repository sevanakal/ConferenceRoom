using System.Security.Claims;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim.Kullanicilar;

/// <summary>
/// Yönetim kullanıcısı ekleme ve düzenleme (/yonetim/kullanicilar/duzenle/{id?}); id yoksa yeni kullanıcı.
/// Silme (OnPostDeleteAsync, onay sorulur): Yönetici rolündeki kullanıcı ve kişinin kendisi silinemez. Silinen kullanıcının
/// satırı kalır, geçmiş satışlarda adı görünür (StaffAccounts.DeleteAsync).
/// Kayıt ve silme bir veritabanı işlemi içinde yapılır; sonunda kullanıcıları yönetebilen aktif kimse kalmıyorsa geri alınır.
/// </summary>
public class DuzenleModel(AppDbContext db, PasswordService passwords) : PageModel
{
    public sealed class UserInput
    {
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        public bool IsActive { get; set; } = true;
        public bool SeatsRestricted { get; set; }
        public List<int> RoleIds { get; set; } = [];
    }

    public sealed record RoleOption(int Id, string Name, string? Description);

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty]
    public UserInput Input { get; set; } = new();

    public IReadOnlyList<RoleOption> Roles { get; private set; } = [];

    /// <summary>Düzenlenen kullanıcı, giriş yapmış kullanıcının kendisi mi (kendini pasif yapamaz).</summary>
    public bool IsSelf => Id is not null && User.FindFirstValue(ClaimTypes.NameIdentifier) == Id.ToString();

    /// <summary>Düzenlenen kullanıcı silinebilir mi (Yönetici rolünde değil ve kişinin kendisi değil).</summary>
    public bool CanDelete { get; private set; }

    /// <summary>Alan adı ya da "" (formun geneli) → hata metninin dil anahtarı.</summary>
    public Dictionary<string, string> Errors { get; } = [];

    [TempData]
    public string? Flash { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Id is not null)
        {
            var user = await FindAsync(cancellationToken);
            if (user is null)
                return NotFound();
            Input = InputFrom(user);
            CanDelete = !IsSelf && StaffAccounts.CanDelete(user);
        }
        await LoadRolesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = Id is null ? null : await FindAsync(cancellationToken);
        if (Id is not null && user is null)
            return NotFound();
        await LoadRolesAsync(cancellationToken);
        CanDelete = user is not null && !IsSelf && StaffAccounts.CanDelete(user);

        // ---- Doğrulama ----
        var fullName = Input.FullName?.Trim() ?? "";
        var email = PasswordService.NormalizeEmail(Input.Email ?? "");
        var password = Input.Password ?? "";
        if (fullName.Length is 0 or > 200) Errors["FullName"] = "auth.required";
        if (!PasswordService.IsValidEmail(email) || email.Length > 254) Errors["Email"] = "auth.emailInvalid";
        else if (await db.StaffUsers.AnyAsync(u => u.OrganizationId == Organization.DefaultId && u.Email == email && u.Id != (Id ?? 0), cancellationToken))
            Errors["Email"] = "users.emailTaken";
        // Yeni kullanıcıda şifre zorunlu; düzenlemede boş bırakılırsa değişmez.
        if ((user is null || password.Length > 0) && password.Length < PasswordService.MinimumLength) Errors["Password"] = "users.passwordShort";
        if (IsSelf && !Input.IsActive) Errors["IsActive"] = "users.selfDeactivate";
        if (Errors.Count > 0)
            return Page();

        // ---- Kayıt ----
        user ??= db.StaffUsers.Add(new StaffUser { OrganizationId = Organization.DefaultId, Email = email, FullName = fullName, PasswordHash = "" }).Entity;
        user.FullName = fullName;
        user.Email = email;
        user.IsActive = Input.IsActive;
        user.SeatsRestricted = Input.SeatsRestricted;
        if (password.Length > 0)
            user.PasswordHash = passwords.Hash(password);
        var selected = Roles.Select(r => r.Id).Where(Input.RoleIds.Contains).ToHashSet();   // yalnızca kurumun rolleri
        user.Roles = user.Roles.Where(r => selected.Contains(r.RoleId)).ToList();
        foreach (var roleId in selected.Where(id => user.Roles.All(r => r.RoleId != id)))
            user.Roles.Add(new StaffUserRole { RoleId = roleId });

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Aynı anda aynı e-postayla iki kayıt: benzersiz indeks ikincisini reddeder.
            Errors["Email"] = "users.emailTaken";
            return Page();
        }
        if (await StaffAccounts.CountManagersAsync(db, cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            Errors[""] = "admin.lastManager";
            return Page();
        }
        await transaction.CommitAsync(cancellationToken);

        Flash = "users.saved";
        return Redirect("/yonetim/kullanicilar");
    }

    // ---- Silme (liste satırındaki ve formun altındaki "Sil" düğmesi; onayı tarayıcı sorar, forms.js) ----

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var user = Id is null ? null : await FindAsync(cancellationToken);
        if (user is null)
            return NotFound();
        await LoadRolesAsync(cancellationToken);
        Input = InputFrom(user);   // silinemezse form kullanıcının mevcut hâliyle yeniden gösterilir
        CanDelete = !IsSelf && StaffAccounts.CanDelete(user);
        if (IsSelf)
            Errors[""] = "users.selfDelete";
        else if (!CanDelete)
            Errors[""] = "users.adminNoDelete";
        if (Errors.Count > 0)
            return Page();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await StaffAccounts.DeleteAsync(db, user, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (await StaffAccounts.CountManagersAsync(db, cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            Errors[""] = "admin.lastManager";
            return Page();
        }
        await transaction.CommitAsync(cancellationToken);

        Flash = "users.deleted";
        return Redirect("/yonetim/kullanicilar");
    }

    // ---- Yardımcılar ----

    private static UserInput InputFrom(StaffUser user) => new()
    {
        FullName = user.FullName,
        Email = user.Email,
        IsActive = user.IsActive,
        SeatsRestricted = user.SeatsRestricted,
        RoleIds = user.Roles.Select(r => r.RoleId).ToList(),
    };

    /// <summary>Kurumun silinmemiş kullanıcısı; silinmiş kullanıcının sayfası yoktur (404).</summary>
    private Task<StaffUser?> FindAsync(CancellationToken cancellationToken) => db.StaffUsers
        .Include(u => u.Roles)
        .FirstOrDefaultAsync(u => u.Id == Id && u.OrganizationId == Organization.DefaultId && u.DeletedAtUtc == null, cancellationToken);

    private async Task LoadRolesAsync(CancellationToken cancellationToken) => Roles = await db.Roles
        .Where(r => r.OrganizationId == Organization.DefaultId)
        .OrderBy(r => r.Name)
        .Select(r => new RoleOption(r.Id, r.Name, r.Description))
        .ToListAsync(cancellationToken);
}
