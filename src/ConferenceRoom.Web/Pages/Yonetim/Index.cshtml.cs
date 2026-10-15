using System.Security.Claims;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceRoom.Web.Pages.Yonetim;

/// <summary>
/// Yönetim paneli ana sayfası. Her yetki bir bölüm kartı olarak görünür; kullanıcının rolünde
/// olmayan bölümler kilitli gösterilir. Bölümler yapıldıkça kartlar ilgili sayfaya bağlanacak.
/// </summary>
public class IndexModel : PageModel
{
    public string FullName { get; private set; } = "";

    public IReadOnlyList<string> RoleNames { get; private set; } = [];

    /// <summary>Panelde gösterilecek bölümler: yetki kodu ve kullanıcının bu yetkiye sahip olup olmadığı.</summary>
    public IReadOnlyList<(string Permission, bool Allowed)> Sections { get; private set; } = [];

    public void OnGet()
    {
        FullName = User.Identity?.Name ?? "";
        RoleNames = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Sections = Permissions.All.Select(p => (p, User.HasClaim(AppClaims.Permission, p))).ToList();
    }
}
