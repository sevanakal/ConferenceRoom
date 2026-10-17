using System.Security.Claims;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim;

/// <summary>
/// Yönetim paneli ana sayfası. Her yetki bir bölüm kartı olarak görünür; kullanıcının rolünde
/// olmayan bölümler kilitli gösterilir. Bölümler yapıldıkça kartlar ilgili sayfaya bağlanacak.
/// Bir yetkinin altındaki ayrı bölümler de kendi kartıyla görünür (<see cref="ExtraSections"/>, ör. satış yetkileri).
/// Kartların sırası kullanıcı başınadır: kullanıcı kartları sürükleyip bırakır (wwwroot/js/panel-duzen.js), sıra
/// <see cref="StaffUser.DashboardOrder"/> alanına kaydedilir (OnPostOrderAsync).
/// </summary>
public class IndexModel(AppDbContext db) : PageModel
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

    /// <summary>Kullanıcı kartları kendi sırasına dizmiş mi (değilse varsayılan sıra; "varsayılana dön" gizlenir).</summary>
    public bool HasCustomOrder { get; private set; }

    /// <summary>Tüm kart anahtarları, varsayılan sırayla: önce yetkiler, sonra ek bölümler.</summary>
    public static IEnumerable<string> CardKeys => Permissions.All.Concat(ExtraSections.Select(x => x.Key));

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        FullName = User.Identity?.Name ?? "";
        RoleNames = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var sections = Permissions.All.Select(p => (p, $"permission.{p}", User.HasClaim(AppClaims.Permission, p)))
            .Concat(ExtraSections.Select(x => (x.Key, $"admin.card.{x.Key}", User.HasClaim(AppClaims.Permission, x.Permission))))
            .ToList();

        // Kayıtlı sıradaki kartlar önce; listede olmayanlar (yeni eklenen bölümler) varsayılan sırayla sona.
        var saved = await db.StaffUsers.Where(u => u.Id == User.UserId()).Select(u => u.DashboardOrder).SingleOrDefaultAsync(cancellationToken);
        HasCustomOrder = saved is not null;
        var rank = (saved ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select((key, i) => (key, i))
            .DistinctBy(x => x.key).ToDictionary(x => x.key, x => x.i);
        Sections = [.. sections.OrderBy(s => rank.GetValueOrDefault(s.Item1, int.MaxValue))];
    }

    /// <summary>
    /// Kart sırasını kaydeder. Gövde: kart anahtarları dizisi (JSON), boş dizi varsayılan sıraya döndürür.
    /// Bilinmeyen anahtarlar ve tekrarlar atılır. Cevap 204.
    /// </summary>
    public async Task<IActionResult> OnPostOrderAsync([FromBody] string[]? keys, CancellationToken cancellationToken)
    {
        var known = CardKeys.ToHashSet();
        var order = (keys ?? []).Where(known.Contains).Distinct().ToList();
        var user = await db.StaffUsers.SingleAsync(u => u.Id == User.UserId(), cancellationToken);
        user.DashboardOrder = order.Count == 0 ? null : string.Join(',', order);
        await db.SaveChangesAsync(cancellationToken);
        return new NoContentResult();
    }
}
