using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Events;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceRoom.Web.Pages.Satis;

/// <summary>
/// Satış bölümünün açılış sayfası (/satis). Satış yetkisi olan personel /giris sayfasından girince buraya gelir
/// (çerez: AuthSchemes.Sales; yönetim paneli bu oturumla açılmaz). Yaklaşan seansları listeler; satışa açık seansın
/// satış ekranı /satis/seans/{seansId} adresindedir (Seans.cshtml).
/// </summary>
public class IndexModel(AppDbContext db, TimeProvider clock) : PageModel
{
    public IReadOnlyList<BoardQuery.Item> Sessions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Sessions = await BoardQuery.LoadAsync(db, clock.GetUtcNow().UtcDateTime, cancellationToken);
}
