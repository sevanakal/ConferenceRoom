using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Web.Auth;
using ConferenceRoom.Web.Common;
using ConferenceRoom.Web.Events;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages.Yonetim.Etkinlikler;

/// <summary>
/// Etkinlik ekleme ve düzenleme (/yonetim/etkinlikler/duzenle/{id?}); id yoksa yeni etkinlik.
/// Sayfanın iki bölümü var:
///   1. Etkinlik bilgileri ve satış kuralları (OnPostAsync). Yeni etkinlik taslak olarak kaydedilir ve seans eklemek için bu sayfaya dönülür.
///   2. Seanslar (OnPostAddSessionAsync, OnPostDeleteSessionAsync). Seans eklendiği anda salonun güncel kroki sürümüne bağlanır.
/// Saatler formda Türkiye saatiyle girilir, veritabanında UTC saklanır (Common/DisplayTime.cs).
/// </summary>
public class DuzenleModel(AppDbContext db, PosterStore posters) : PageModel
{
    public sealed class EventInput
    {
        public string? Title { get; set; }
        public string? Organizer { get; set; }
        public string? Description { get; set; }
        public int EventTypeId { get; set; }
        public int HallId { get; set; }
        public EventStatus Status { get; set; } = EventStatus.Draft;
        public SaleMode SaleMode { get; set; } = SaleMode.Reservation;
        public bool RequiresApproval { get; set; }
        public bool OnlineEnabled { get; set; } = true;
        public DateTime? SalesStart { get; set; }
        public DateTime? SalesEnd { get; set; }
        public int? MaxTicketsPerPerson { get; set; }
        public IFormFile? Poster { get; set; }
        public bool RemovePoster { get; set; }
    }

    public sealed class SessionInput
    {
        public DateOnly? Date { get; set; }
        public TimeOnly? Start { get; set; }
        public TimeOnly? End { get; set; }
        public TimeOnly? DoorsOpen { get; set; }
    }

    public sealed record Option(int Id, string Name, bool IsActive);
    public sealed record TypeOption(int Id, string Name, bool IsActive, SaleMode SaleMode, bool RequiresApproval);
    public sealed record SessionRow(int Id, DateTime StartsAtUtc, DateTime? EndsAtUtc, DateTime? DoorsOpenAtUtc, int PlanVersion);

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty]
    public EventInput Input { get; set; } = new();

    [BindProperty]
    public SessionInput NewSession { get; set; } = new();

    public IReadOnlyList<TypeOption> Types { get; private set; } = [];
    public IReadOnlyList<Option> Halls { get; private set; } = [];
    public IReadOnlyList<SessionRow> Sessions { get; private set; } = [];

    /// <summary>Kayıtlı afişin dosya adı (düzenlemede gösterilir).</summary>
    public string? PosterFileName { get; private set; }

    /// <summary>Alan adı → hata metninin dil anahtarı. Seans formunun hataları "Session." önekiyle.</summary>
    public Dictionary<string, string> Errors { get; } = [];

    [TempData]
    public string? Flash { get; set; }

    // ---- Açılış ----

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Id is null)
        {
            await LoadOptionsAsync(cancellationToken);
            // Yeni etkinlik: ilk türün varsayılanları (tür değişince sayfadaki betik de aynısını yapar).
            if (Types.FirstOrDefault(t => t.IsActive) is { } first)
                (Input.EventTypeId, Input.SaleMode, Input.RequiresApproval) = (first.Id, first.SaleMode, first.RequiresApproval);
            return Page();
        }

        var ev = await FindAsync(cancellationToken);
        if (ev is null)
            return NotFound();
        return await ShowAsync(ev, cancellationToken);
    }

    // ---- Etkinlik bilgileri ----

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var ev = Id is null ? null : await FindAsync(cancellationToken);
        if (Id is not null && ev is null)
            return NotFound();
        PosterFileName = ev?.PosterFileName;
        await LoadOptionsAsync(cancellationToken);
        await LoadSessionsAsync(cancellationToken);

        // ---- Doğrulama ----
        var title = Input.Title?.Trim() ?? "";
        if (title.Length is 0 or > 200)
            Errors["Title"] = "events.titleRequired";
        // Pasif tür/salon yeni seçilemez; etkinliğin zaten bağlı olduğu kalabilir.
        if (!Types.Any(t => t.Id == Input.EventTypeId && (t.IsActive || t.Id == ev?.EventTypeId)))
            Errors["EventTypeId"] = "events.typeRequired";
        if (!Halls.Any(h => h.Id == Input.HallId && (h.IsActive || h.Id == ev?.HallId)))
            Errors["HallId"] = "events.hallRequired";
        else if (ev is not null && ev.HallId != Input.HallId && Sessions.Count > 0)
            Errors["HallId"] = "events.hallLocked";   // seanslar eski salonun kroki sürümüne bağlı
        if (!Enum.IsDefined(Input.Status))
            Input.Status = EventStatus.Draft;
        if (!Enum.IsDefined(Input.SaleMode))
            Input.SaleMode = SaleMode.Reservation;
        if (Input.Status is EventStatus.Published or EventStatus.SalesClosed && Sessions.Count == 0)
            Errors["Status"] = "events.publishNeedsSession";
        if (Input.SalesStart is not null && Input.SalesEnd is not null && Input.SalesEnd <= Input.SalesStart)
            Errors["SalesEnd"] = "events.salesEndBeforeStart";
        if (Input.MaxTicketsPerPerson is < 1 or > 100)
            Errors["MaxTicketsPerPerson"] = "events.maxTicketsRange";

        string? newPoster = null;
        if (Errors.Count == 0 && Input.Poster is { Length: > 0 } file)
        {
            (newPoster, var posterError) = await posters.SaveAsync(file, cancellationToken);
            if (posterError is not null)
                Errors["Poster"] = posterError;
        }
        if (Errors.Count > 0)
            return Page();

        // ---- Kayıt ----
        var isNew = ev is null;
        ev ??= db.Events.Add(new Event { OrganizationId = Organization.DefaultId, Title = title, CreatedByStaffUserId = User.UserId() }).Entity;
        ev.Title = title;
        ev.Organizer = Clean(Input.Organizer, 200);
        ev.Description = Clean(Input.Description, 4000);
        ev.EventTypeId = Input.EventTypeId;
        ev.HallId = Input.HallId;
        ev.Status = Input.Status;
        ev.SaleMode = Input.SaleMode;
        ev.RequiresApproval = Input.RequiresApproval;
        ev.OnlineEnabled = Input.OnlineEnabled;
        ev.SalesStartUtc = Input.SalesStart is { } start ? DisplayTime.ToUtc(start) : null;
        ev.SalesEndUtc = Input.SalesEnd is { } end ? DisplayTime.ToUtc(end) : null;
        ev.MaxTicketsPerPerson = Input.MaxTicketsPerPerson;
        var oldPoster = ev.PosterFileName;
        if (newPoster is not null || Input.RemovePoster)
            ev.PosterFileName = newPoster;
        await db.SaveChangesAsync(cancellationToken);
        if (oldPoster != ev.PosterFileName)
            posters.Delete(oldPoster);   // eski afiş ancak kayıt başarılı olunca silinir

        Flash = isNew ? "events.createdAddSessions" : "events.saved";
        return Redirect(isNew ? $"/yonetim/etkinlikler/duzenle/{ev.Id}#seanslar" : "/yonetim/etkinlikler");
    }

    // ---- Seanslar ----

    public async Task<IActionResult> OnPostAddSessionAsync(CancellationToken cancellationToken)
    {
        var ev = await FindAsync(cancellationToken);
        if (ev is null)
            return NotFound();

        // Seans, salonun o anki güncel kroki sürümüne bağlanır.
        var planVersionId = await db.HallPlanVersions
            .Where(v => v.HallId == ev.HallId)
            .OrderByDescending(v => v.Version)
            .Select(v => (int?)v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var s = NewSession;
        if (s.Date is null || s.Start is null)
            Errors["Session.Start"] = "events.sessionStartRequired";
        else if (planVersionId is null)
            Errors["Session.Start"] = "events.hallHasNoPlan";
        if (Errors.Count > 0)
            return await RedisplayAsync(ev, cancellationToken);

        var startLocal = s.Date!.Value.ToDateTime(s.Start!.Value);
        // Bitiş başlangıçtan önceyse ertesi gün sayılır (ör. 22:00 – 01:00).
        DateTime? endLocal = s.End is { } e ? s.Date.Value.ToDateTime(e) : null;
        if (endLocal <= startLocal)
            endLocal = endLocal.Value.AddDays(1);
        DateTime? doorsLocal = s.DoorsOpen is { } d ? s.Date.Value.ToDateTime(d) : null;
        if (doorsLocal > startLocal)
        {
            Errors["Session.DoorsOpen"] = "events.doorsAfterStart";
            return await RedisplayAsync(ev, cancellationToken);
        }

        db.EventSessions.Add(new EventSession
        {
            EventId = ev.Id,
            StartsAtUtc = DisplayTime.ToUtc(startLocal),
            EndsAtUtc = endLocal is { } el ? DisplayTime.ToUtc(el) : null,
            DoorsOpenAtUtc = doorsLocal is { } dl ? DisplayTime.ToUtc(dl) : null,
            HallPlanVersionId = planVersionId!.Value,
        });
        await db.SaveChangesAsync(cancellationToken);
        Flash = "events.sessionAdded";
        return Redirect($"/yonetim/etkinlikler/duzenle/{ev.Id}#seanslar");
    }

    // Not: bilet satışı eklendiğinde satışı olan seans silinemeyecek; o zaman burada denetlenecek.
    public async Task<IActionResult> OnPostDeleteSessionAsync(int sessionId, CancellationToken cancellationToken)
    {
        var ev = await FindAsync(cancellationToken);
        if (ev is null)
            return NotFound();
        var session = await db.EventSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.EventId == ev.Id, cancellationToken);
        if (session is null)
            return NotFound();

        // Yayındaki etkinliğin son seansı silinirse panoda tarihsiz bir etkinlik kalırdı; önce taslağa alınmalı.
        var remaining = await db.EventSessions.CountAsync(x => x.EventId == ev.Id, cancellationToken) - 1;
        if (remaining == 0 && ev.Status is EventStatus.Published or EventStatus.SalesClosed)
        {
            Errors["Session.Delete"] = "events.lastSessionPublished";
            return await RedisplayAsync(ev, cancellationToken);
        }

        db.EventSessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);
        Flash = "events.sessionDeleted";
        return Redirect($"/yonetim/etkinlikler/duzenle/{ev.Id}#seanslar");
    }

    // ---- Yardımcılar ----

    private Task<Event?> FindAsync(CancellationToken cancellationToken) =>
        db.Events.FirstOrDefaultAsync(e => e.Id == Id && e.OrganizationId == Organization.DefaultId, cancellationToken);


    /// <summary>Seans formunda hata olduğunda sayfayı etkinliğin kayıtlı bilgileriyle yeniden gösterir.</summary>
    private Task<IActionResult> RedisplayAsync(Event ev, CancellationToken cancellationToken)
    {
        ModelState.Clear();
        return ShowAsync(ev, cancellationToken);
    }

    /// <summary>Formu etkinliğin kayıtlı bilgileriyle doldurup sayfayı gösterir.</summary>
    private async Task<IActionResult> ShowAsync(Event ev, CancellationToken cancellationToken)
    {
        Input = new EventInput
        {
            Title = ev.Title,
            Organizer = ev.Organizer,
            Description = ev.Description,
            EventTypeId = ev.EventTypeId,
            HallId = ev.HallId,
            Status = ev.Status,
            SaleMode = ev.SaleMode,
            RequiresApproval = ev.RequiresApproval,
            OnlineEnabled = ev.OnlineEnabled,
            SalesStart = ev.SalesStartUtc is { } s ? DisplayTime.ToLocal(s) : null,
            SalesEnd = ev.SalesEndUtc is { } e ? DisplayTime.ToLocal(e) : null,
            MaxTicketsPerPerson = ev.MaxTicketsPerPerson,
        };
        PosterFileName = ev.PosterFileName;
        await LoadOptionsAsync(cancellationToken);
        await LoadSessionsAsync(cancellationToken);
        return Page();
    }

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        Types = await db.EventTypes
            .Where(t => t.OrganizationId == Organization.DefaultId)
            .OrderBy(t => t.Name)
            .Select(t => new TypeOption(t.Id, t.Name, t.IsActive, t.DefaultSaleMode, t.DefaultRequiresApproval))
            .ToListAsync(cancellationToken);
        Halls = await db.Halls
            .Where(h => h.OrganizationId == Organization.DefaultId)
            .OrderBy(h => h.Name)
            .Select(h => new Option(h.Id, h.Name, h.IsActive))
            .ToListAsync(cancellationToken);
    }

    private async Task LoadSessionsAsync(CancellationToken cancellationToken)
    {
        if (Id is null)
            return;
        Sessions = await db.EventSessions
            .Where(s => s.EventId == Id)
            .OrderBy(s => s.StartsAtUtc)
            .Select(s => new SessionRow(s.Id, s.StartsAtUtc, s.EndsAtUtc, s.DoorsOpenAtUtc, s.HallPlanVersion!.Version))
            .ToListAsync(cancellationToken);
    }

    private static string? Clean(string? value, int max)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text[..Math.Min(text.Length, max)];
    }
}
