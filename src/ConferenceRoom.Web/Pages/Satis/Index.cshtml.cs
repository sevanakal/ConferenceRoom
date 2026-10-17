using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Events;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceRoom.Web.Pages.Satis;

/// <summary>
/// Satış bölümünün açılış sayfası (/satis). Satış yetkisi olan personel /giris sayfasından girince buraya gelir
/// (çerez: AuthSchemes.Sales; yönetim paneli bu oturumla açılmaz). Şimdilik satılabilecek yaklaşan seansları listeler;
/// koltuk seçerek satış ekranı bir sonraki adımda buraya eklenecek.
/// </summary>
public class IndexModel(AppDbContext db, TimeProvider clock) : PageModel
{
    public IReadOnlyList<BoardQuery.Item> Sessions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Sessions = await BoardQuery.LoadAsync(db, clock.GetUtcNow().UtcDateTime, cancellationToken);
}
