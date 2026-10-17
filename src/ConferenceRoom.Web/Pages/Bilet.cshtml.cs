using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages;

/// <summary>
/// Panodaki "Bilet al" düğmesinin açtığı seans sayfası (/bilet/{seansId}). Giriş istemez.
/// Şimdilik seansın bilgisini ve online satışın yakında açılacağını gösterir; koltuk seçip bilet alma
/// (hesap bilgisi bu adımda istenir) 4. aşamada buraya gelecek. Taslak etkinliğin seansı bulunamaz.
/// </summary>
public class BiletModel(AppDbContext db) : PageModel
{
    public sealed record Info(string Title, string HallName, DateTime StartsAtUtc, string? PosterFileName);

    public Info Session { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int sessionId, CancellationToken cancellationToken)
    {
        var info = await db.EventSessions
            .Where(s => s.Id == sessionId)
            .Join(db.Events, s => s.EventId, e => e.Id, (s, e) => new { s, e })
            .Where(x => x.e.OrganizationId == Organization.DefaultId && x.e.Status != EventStatus.Draft)
            .Select(x => new Info(x.e.Title, x.e.Hall!.Name, x.s.StartsAtUtc, x.e.PosterFileName))
            .FirstOrDefaultAsync(cancellationToken);
        if (info is null)
            return NotFound();
        Session = info;
        return Page();
    }
}
