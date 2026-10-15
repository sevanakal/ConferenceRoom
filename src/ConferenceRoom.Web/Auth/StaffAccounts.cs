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

    // ---- Çerez doğrulaması ----

    /// <summary>
    /// Yönetim çerezi her istekte veritabanıyla karşılaştırılır:
    ///   - kullanıcı silinmiş ya da pasif yapılmışsa oturum hemen kapanır,
    ///   - rolleri, yetkileri ya da adı değişmişse çerez yenilenir; yeni yetkiler yeniden giriş gerekmeden geçerli olur.
    /// Panel az sayıda kişi tarafından kullanıldığı için istek başına bir sorgu sorun değildir.
    /// </summary>
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = int.TryParse(id, out var userId)
            ? await db.StaffUsers.WithPermissions().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, context.HttpContext.RequestAborted)
            : null;

        if (user is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(AuthSchemes.Staff);
            return;
        }

        var fresh = Principals.ForStaff(user);
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
