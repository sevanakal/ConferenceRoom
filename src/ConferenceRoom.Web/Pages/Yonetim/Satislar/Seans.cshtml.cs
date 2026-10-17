using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using ConferenceRoom.Web.Sales;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Web.Pages.Yonetim.Satislar;

/// <summary>
/// Seansın satış ayrıntısı (/yonetim/satislar/seans/{id}): raporun o seansa ait satırı ve işlem listesi (zaman, alıcı,
/// telefon, koltuklar, tutar, ödeme, satan personel, açıklama). Bildirimlerden de buraya gelinir. "sales.view" yetkisi ister.
/// "Satış kayıtlarını düzeltme" (sales.manage) yetkisi olan kullanıcı burada herhangi bir işlemin ödeme durumunu değiştirir
/// (Payment işleyicisi; kural SalesDesk.SetPaymentAsync). "?handler=Excel" işlem listesini Excel dosyası olarak indirir.
/// </summary>
public class SeansModel(AppDbContext db, TimeProvider clock, IStringLocalizer localizer) : PageModel
{
    /// <summary>Kullanıcı ödeme durumunu değiştirebilir mi (düğmeler yalnızca o zaman görünür).</summary>
    public bool CanManage => User.HasClaim(AppClaims.Permission, Permissions.SalesManage);

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public SalesReport.SessionRow Session { get; private set; } = null!;

    public IReadOnlyList<SalesReport.OrderRow> Orders { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken) ? Page() : NotFound();

    /// <summary>Seansın işlemlerini .xlsx olarak indirir.</summary>
    public async Task<IActionResult> OnGetExcelAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken)
            ? File(SalesExcel.Orders(Session, Orders, localizer), SalesExcel.ContentType, $"satislar-seans-{Id}.xlsx")
            : NotFound();

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var row = (await SalesReport.SessionsAsync(db, includePast: true, clock.GetUtcNow().UtcDateTime, cancellationToken, sessionId: Id)).SingleOrDefault();
        if (row is null)
            return false;
        Session = row;
        Orders = await SalesReport.OrdersAsync(db, Id, cancellationToken);
        return true;
    }

    /// <summary>Ödeme durumunu değiştirir ve işlemin satırına döner. Yetkisi olmayan kullanıcıya 403.</summary>
    public async Task<IActionResult> OnPostPaymentAsync(int orderId, bool paid, CancellationToken cancellationToken)
    {
        if (!CanManage)
            return Forbid();
        await SalesDesk.SetPaymentAsync(db, User.UserId(), orderId, paid, cancellationToken, anySeller: true);
        return Redirect($"/yonetim/satislar/seans/{Id}#islem-{orderId}");
    }
}
