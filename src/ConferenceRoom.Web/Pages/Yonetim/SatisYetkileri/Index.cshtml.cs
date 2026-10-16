using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Domain.Sales;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Events;
using ConferenceRoom.Web.Sales;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim.SatisYetkileri;

/// <summary>
/// Satış yetkileri (/yonetim/satis-yetkileri): satış yapabilen personel (tam yetkili ya da koltuk kısıtlamalı),
/// salonlar ve etkinlikler. Salon satırından salonun genel koltuk kapsamına, etkinlik satırından o etkinliğe özel
/// kapsama gidilir (Koltuklar.cshtml). "users.manage" yetkisi ister (Program.cs).
/// </summary>
public class IndexModel(AppDbContext db, TimeProvider clock) : PageModel
{
    public sealed record HallRow(int Id, string Name, bool HasPlan, int StaffCount);

    public sealed record EventRow(int Id, string Title, string HallName, DateTime? FirstStartUtc, bool IsEventSpecific);

    public IReadOnlyList<SeatScopeStore.StaffRow> Sellers { get; private set; } = [];

    public IReadOnlyList<HallRow> Halls { get; private set; } = [];

    public IReadOnlyList<EventRow> Events { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Sellers = await SeatScopeStore.SellersAsync(db, Organization.DefaultId, cancellationToken);

        // ---- Salonlar: krokisi var mı, genel kayıtta kaç personele koltuk verilmiş ----
        var halls = await db.Halls
            .Where(h => h.OrganizationId == Organization.DefaultId && h.IsActive)
            .OrderBy(h => h.Name)
            .Select(h => new
            {
                h.Id,
                h.Name,
                HasPlan = h.PlanVersions.Any(),
                Scope = db.SeatScopes.Where(s => s.HallId == h.Id && s.EventId == null).Select(s => s.ScopeJson).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        Halls = halls.Select(h => new HallRow(h.Id, h.Name, h.HasPlan, h.Scope is null ? 0 : SeatScopeData.StaffCount(h.Scope))).ToList();

        // ---- Etkinlikler: iptal ve tamamlananlar dışında; etkinliğe özel kayıt var mı ----
        var rows = await db.Events
            .Where(e => e.OrganizationId == Organization.DefaultId && e.Status != EventStatus.Cancelled)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.Status,
                HallName = e.Hall!.Name,
                FirstStartUtc = e.Sessions.Min(s => (DateTime?)s.StartsAtUtc),
                LastEndUtc = e.Sessions.Max(s => (DateTime?)(s.EndsAtUtc ?? s.StartsAtUtc)),
                IsEventSpecific = db.SeatScopes.Any(s => s.EventId == e.Id),
            })
            .ToListAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        Events = rows
            .Where(r => EventStates.Of(r.Status, r.LastEndUtc, now) != EventState.Completed)
            .OrderBy(r => r.FirstStartUtc ?? DateTime.MaxValue)
            .Select(r => new EventRow(r.Id, r.Title, r.HallName, r.FirstStartUtc, r.IsEventSpecific))
            .ToList();
    }
}
