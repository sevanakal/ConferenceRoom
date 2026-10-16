using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Events;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim.Etkinlikler;

/// <summary>
/// Etkinlik listesi (/yonetim/etkinlikler). Yaklaşan ve hazırlanan etkinlikler üstte, tamamlanan ve iptal edilenler altta.
/// "events.manage" yetkisi ister (Program.cs).
/// </summary>
public class IndexModel(AppDbContext db, TimeProvider clock) : PageModel
{
    public sealed record EventRow(int Id, string Title, string? Organizer, string TypeName, string HallName, int SessionCount, DateTime? FirstStartUtc, DateTime? LastStartUtc, EventState State);

    public IReadOnlyList<EventRow> Events { get; private set; } = [];

    [TempData]
    public string? Flash { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Events
            .Where(e => e.OrganizationId == Organization.DefaultId)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.Organizer,
                e.Status,
                TypeName = e.EventType!.Name,
                HallName = e.Hall!.Name,
                SessionCount = e.Sessions.Count,
                FirstStartUtc = e.Sessions.Min(s => (DateTime?)s.StartsAtUtc),
                LastStartUtc = e.Sessions.Max(s => (DateTime?)s.StartsAtUtc),
                LastEndUtc = e.Sessions.Max(s => (DateTime?)(s.EndsAtUtc ?? s.StartsAtUtc)),
            })
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        Events = rows
            .Select(r => new EventRow(r.Id, r.Title, r.Organizer, r.TypeName, r.HallName, r.SessionCount, r.FirstStartUtc, r.LastStartUtc,
                EventStates.Of(r.Status, r.LastEndUtc, now)))
            .OrderBy(r => r.State is EventState.Completed or EventState.Cancelled)
            .ThenBy(r => r.FirstStartUtc ?? DateTime.MaxValue)
            .ToList();
    }

    public string PillClass(EventState state) => state switch
    {
        EventState.Published => "pill-ok",
        EventState.Draft => "pill-soft",
        _ => "pill-locked",
    };
}
