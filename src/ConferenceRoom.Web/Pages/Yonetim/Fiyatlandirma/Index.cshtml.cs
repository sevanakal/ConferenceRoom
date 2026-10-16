using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Events;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim.Fiyatlandirma;

/// <summary>
/// Fiyatlandırma (/yonetim/fiyatlandirma): etkinlikler ve koltuk ayarlarının özeti (kaç koltuk kategorili, kaç koltuk online).
/// Her satırdan etkinliğin koltuk ayarları ekranına (Koltuklar.cshtml) gidilir. "pricing.manage" yetkisi ister (Program.cs).
/// Tamamlanan ve iptal edilen etkinlikler listelenmez.
/// </summary>
public class IndexModel(AppDbContext db, TimeProvider clock) : PageModel
{
    public sealed record EventRow(int Id, string Title, string HallName, int SessionCount, DateTime? FirstStartUtc, EventState State, bool OnlineEnabled, int? AssignedSeats, int? OnlineSeats);

    public IReadOnlyList<EventRow> Events { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Events
            .Where(e => e.OrganizationId == Organization.DefaultId && e.Status != EventStatus.Cancelled)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.Status,
                e.OnlineEnabled,
                HallName = e.Hall!.Name,
                SessionCount = e.Sessions.Count,
                FirstStartUtc = e.Sessions.Min(s => (DateTime?)s.StartsAtUtc),
                LastEndUtc = e.Sessions.Max(s => (DateTime?)(s.EndsAtUtc ?? s.StartsAtUtc)),
                Map = db.EventSeatMaps.Where(m => m.EventId == e.Id).Select(m => new { m.AssignedSeats, m.OnlineSeats }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        Events = rows
            .Select(r => new EventRow(r.Id, r.Title, r.HallName, r.SessionCount, r.FirstStartUtc, EventStates.Of(r.Status, r.LastEndUtc, now),
                r.OnlineEnabled, r.Map?.AssignedSeats, r.Map?.OnlineSeats))
            .Where(r => r.State != EventState.Completed)
            .OrderBy(r => r.FirstStartUtc ?? DateTime.MaxValue)
            .ToList();
    }
}
