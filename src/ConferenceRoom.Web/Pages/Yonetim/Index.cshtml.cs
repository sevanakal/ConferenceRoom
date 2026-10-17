using System.Security.Claims;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceRoom.Web.Pages.Yonetim;

/// <summary>
/// Yönetim paneli ana sayfası. Her yetki bir bölüm kartı olarak görünür; kullanıcının rolünde
/// olmayan bölümler kilitli gösterilir. Bölümler yapıldıkça kartlar ilgili sayfaya bağlanacak.
/// Bir yetkinin altındaki ayrı bölümler de kendi kartıyla görünür (<see cref="ExtraSections"/>, ör. satış yetkileri).
/// </summary>
public class IndexModel : PageModel
{
    public string FullName { get; private set; } = "";

    public IReadOnlyList<string> RoleNames { get; private set; } = [];

    /// <summary>
    /// Yetkilerin dışında kart olarak gösterilen bölümler: (kart anahtarı, gereken yetki). Kart metinleri
    /// dil dosyasında "admin.card.&lt;anahtar&gt;.name" ve ".description"; yeni bölüm buraya ve Index.cshtml'deki adreslere eklenir.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Permission)> ExtraSections = [("salesScope", Permissions.UsersManage)];

    /// <summary>
    /// Panelde gösterilecek kartlar: anahtar (yetki kodu ya da ek bölüm anahtarı), metin anahtarlarının öneki
    /// ve kullanıcının bu bölüme girip giremeyeceği.
    /// </summary>
    public IReadOnlyList<(string Key, string TextPrefix, bool Allowed)> Sections { get; private set; } = [];

    public void OnGet()
    {
        FullName = User.Identity?.Name ?? "";
        RoleNames = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Sections = Permissions.All.Select(p => (p, $"permission.{p}", User.HasClaim(AppClaims.Permission, p)))
            .Concat(ExtraSections.Select(x => (x.Key, $"admin.card.{x.Key}", User.HasClaim(AppClaims.Permission, x.Permission))))
            .ToList();
    }
}
