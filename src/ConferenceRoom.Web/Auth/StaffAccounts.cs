using System.Security.Claims;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Auth;

/// <summary>
/// Yönetim kullanıcılarıyla ilgili ortak sorgular ve kurallar. Giriş sayfası, çerez doğrulaması ve
/// kullanıcı/rol yönetimi sayfaları aynı kuralları buradan kullanır ki tek yerde değişsin.
/// </summary>
public static class StaffAccounts
{
    // ---- Sorgular ----

    /// <summary>Kurumun yönetim kullanıcıları, rolleri ve rollerin yetkileriyle birlikte (kimlik bilgisi üretmek için gereken her şey).</summary>
    public static IQueryable<StaffUser> WithPermissions(this IQueryable<StaffUser> users) => users
        .Where(u => u.OrganizationId == Organization.DefaultId)
        .Include(u => u.Roles).ThenInclude(r => r.Role!).ThenInclude(r => r.Permissions)
        .AsSplitQuery();

    /// <summary>
    /// Kullanıcı ve rolleri yönetebilen aktif kullanıcı sayısı. Bir değişiklik bunu sıfıra indirirse
    /// panelde kimse kullanıcı ekleyemez, rol düzenleyemez hale gelir; kaydetme sayfaları bunu engeller.
    /// </summary>
    public static Task<int> CountManagersAsync(AppDbContext db, CancellationToken cancellationToken) => db.StaffUsers
        .CountAsync(u => u.OrganizationId == Organization.DefaultId && u.IsActive
            && u.Roles.Any(r => r.Role!.Permissions.Any(p => p.Permission == Permissions.UsersManage)), cancellationToken);

    // ---- Silme ----

    /// <summary>Silinebilir mi: Yönetici rolündeki kullanıcı silinemez (önce rolü kaldırılmalı). Roles yüklenmiş olmalı.</summary>
    public static bool CanDelete(StaffUser user) => user.Roles.All(r => r.RoleId != Role.AdministratorId);

    /// <summary>
    /// Kullanıcıyı siler. Satır kalır (<see cref="StaffUser.DeletedAtUtc"/>): satışlar, kroki sürümleri ve etkinlikler ona bağlı,
    /// adı geçmiş kayıtlarda görünmeye devam eder. Giriş bilgisi, rolleri, panel sırası ve bildirimleri kaldırılır; e-postası
    /// yeni bir kullanıcıya verilebilsin diye kimliğe bağlı bir adresle değiştirilir. Kayıt çağıranın SaveChanges'ıyla yazılır.
    /// </summary>
    public static async Task DeleteAsync(AppDbContext db, StaffUser user, CancellationToken cancellationToken)
    {
        user.DeletedAtUtc = DateTime.UtcNow;
        user.IsActive = false;   // açık oturumları her istekteki denetimle hemen kapanır
        user.Email = $"silindi-{user.Id}@silinmis.invalid";
        user.PasswordHash = "";
        user.DashboardOrder = null;
        user.Roles.Clear();
        await db.Notifications.Where(n => n.StaffUserId == user.Id).ExecuteDeleteAsync(cancellationToken);
    }

    // ---- Hangi girişi kullanabilir ----

    /// <summary>Kullanıcının rollerinden gelen yetkiler. Roles → Role → Permissions yüklenmiş olmalı.</summary>
    public static IReadOnlySet<string> PermissionsOf(StaffUser user) =>
        user.Roles.SelectMany(r => r.Role!.Permissions).Select(p => p.Permission).ToHashSet();

    /// <summary>
    /// Yönetim paneline (/yonetim/giris) girebilir mi: satış dışında en az bir yetkisi olmalı. Yalnızca satış yapan
    /// personel panele giremez; o, izleyiciyle aynı sayfadan (/giris) satış bölümüne girer.
    /// </summary>
    public static bool CanUsePanel(IReadOnlySet<string> permissions) => permissions.Any(p => p != Permissions.SalesSell);

    /// <summary>Satış bölümüne (/giris → /satis) girebilir mi: "sales.sell" yetkisi olmalı.</summary>
    public static bool CanSell(IReadOnlySet<string> permissions) => permissions.Contains(Permissions.SalesSell);

    // ---- Çerez doğrulaması ----

    /// <summary>Yönetim çerezi: kullanıcı aktif olmalı ve panel yetkisi sürmeli (<see cref="CanUsePanel"/>).</summary>
    public static Task ValidatePanelPrincipalAsync(CookieValidatePrincipalContext context) =>
        ValidateAsync(context, AuthSchemes.Staff, CanUsePanel);

    /// <summary>Satış çerezi: kullanıcı aktif olmalı ve satış yetkisi sürmeli (<see cref="CanSell"/>).</summary>
    public static Task ValidateSalesPrincipalAsync(CookieValidatePrincipalContext context) =>
        ValidateAsync(context, AuthSchemes.Sales, CanSell);

    /// <summary>
    /// Personel çerezi her istekte veritabanıyla karşılaştırılır:
    ///   - kullanıcı silinmiş, pasif yapılmış ya da bu bölümün yetkisi alınmışsa oturum hemen kapanır,
    ///   - rolleri, yetkileri ya da adı değişmişse çerez yenilenir; yeni yetkiler yeniden giriş gerekmeden geçerli olur.
    /// Personel az sayıda olduğu için istek başına bir sorgu sorun değildir.
    /// </summary>
    private static async Task ValidateAsync(CookieValidatePrincipalContext context, string scheme, Func<IReadOnlySet<string>, bool> allowed)
    {
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = int.TryParse(id, out var userId)
            ? await db.StaffUsers.WithPermissions().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, context.HttpContext.RequestAborted)
            : null;

        if (user is null || !allowed(PermissionsOf(user)))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(scheme);
            return;
        }

        var fresh = Principals.ForStaff(user, scheme);
        if (!Fingerprint(fresh).SetEquals(Fingerprint(context.Principal!)))
        {
            context.ReplacePrincipal(fresh);
            context.ShouldRenew = true;
        }
    }

    // Karşılaştırılan alanlar: ad, e-posta, roller ve yetkiler.
    private static HashSet<string> Fingerprint(ClaimsPrincipal principal) => principal.Claims
        .Where(c => c.Type is ClaimTypes.Name or ClaimTypes.Email or ClaimTypes.Role or AppClaims.Permission)
        .Select(c => c.Type + "=" + c.Value)
        .ToHashSet();
}
