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
/// Etkinlik bilgileri, seansları ve satış kuralları tek formda, tek kayıtla saklanır (OnPostAsync).
/// Seanslar formda dinamik satırlardır: "+" ile satır eklenir; kayıtlı seans satırı kaldırılırsa kimliği
/// RemovedSessionIds ile gelir ve kayıtta silinir. Yeni seans, kayıt anında salonun güncel kroki sürümüne bağlanır;
/// kayıtlı seansın kroki sürümü saati değişse de korunur.
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

    /// <summary>Formdaki bir seans satırı. Id yalnızca kayıtlı seansta dolu.</summary>
    public sealed class SessionInput
    {
        public int? Id { get; set; }
        public DateOnly? Date { get; set; }
        public TimeOnly? Start { get; set; }
        public TimeOnly? End { get; set; }
        public TimeOnly? DoorsOpen { get; set; }

        public bool IsBlank => Id is null && Date is null && Start is null && End is null && DoorsOpen is null;
    }

    public sealed record Option(int Id, string Name, bool IsActive);
    public sealed record TypeOption(int Id, string Name, bool IsActive, SaleMode SaleMode, bool RequiresApproval);

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty]
    public EventInput Input { get; set; } = new();

    [BindProperty]
    public List<SessionInput> SessionRows { get; set; } = [];

    /// <summary>Formda kaldırılan kayıtlı seansların kimlikleri; kayıtta silinir.</summary>
    [BindProperty]
    public List<int> RemovedSessionIds { get; set; } = [];

    public IReadOnlyList<TypeOption> Types { get; private set; } = [];
    public IReadOnlyList<Option> Halls { get; private set; } = [];

    /// <summary>Kayıtlı seans kimliği → bağlı olduğu kroki sürüm numarası (satırda gösterilir).</summary>
    public IReadOnlyDictionary<int, int> PlanVersions { get; private set; } = new Dictionary<int, int>();

    /// <summary>Kayıtlı afişin dosya adı (düzenlemede gösterilir).</summary>
    public string? PosterFileName { get; private set; }

    /// <summary>Alan adı → hata metninin dil anahtarı. Seans satırlarının hataları "Session.{sıra}" anahtarıyla.</summary>
    public Dictionary<string, string> Errors { get; } = [];

    [TempData]
    public string? Flash { get; set; }

    // ---- Açılış ----

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadOptionsAsync(cancellationToken);
        if (Id is null)
        {
            // Yeni etkinlik: ilk türün varsayılanları (tür değişince sayfadaki betik de aynısını yapar) ve boş bir seans satırı.
            if (Types.FirstOrDefault(t => t.IsActive) is { } first)
                (Input.EventTypeId, Input.SaleMode, Input.RequiresApproval) = (first.Id, first.SaleMode, first.RequiresApproval);
            SessionRows = [new SessionInput()];
            return Page();
        }

        var ev = await FindAsync(cancellationToken);
        if (ev is null)
            return NotFound();
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
        SessionRows = ev.Sessions.OrderBy(x => x.StartsAtUtc).Select(ToInput).ToList();
        PlanVersions = ev.Sessions.ToDictionary(x => x.Id, x => x.HallPlanVersion!.Version);
        return Page();
    }

    // ---- Kayıt ----

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var ev = Id is null ? null : await FindAsync(cancellationToken);
        if (Id is not null && ev is null)
            return NotFound();
        PosterFileName = ev?.PosterFileName;
        await LoadOptionsAsync(cancellationToken);
        var existing = ev?.Sessions.ToList() ?? [];
        PlanVersions = existing.ToDictionary(x => x.Id, x => x.HallPlanVersion!.Version);

        // ---- Etkinlik bilgileri ----
        var title = Input.Title?.Trim() ?? "";
        if (title.Length is 0 or > 200)
            Errors["Title"] = "events.titleRequired";
        // Pasif tür/salon yeni seçilemez; etkinliğin zaten bağlı olduğu kalabilir.
        if (!Types.Any(t => t.Id == Input.EventTypeId && (t.IsActive || t.Id == ev?.EventTypeId)))
            Errors["EventTypeId"] = "events.typeRequired";
        if (!Halls.Any(h => h.Id == Input.HallId && (h.IsActive || h.Id == ev?.HallId)))
            Errors["HallId"] = "events.hallRequired";
        if (!Enum.IsDefined(Input.Status))
            Input.Status = EventStatus.Draft;
        if (!Enum.IsDefined(Input.SaleMode))
            Input.SaleMode = SaleMode.Reservation;
        if (Input.SalesStart is not null && Input.SalesEnd is not null && Input.SalesEnd <= Input.SalesStart)
            Errors["SalesEnd"] = "events.salesEndBeforeStart";
        if (Input.MaxTicketsPerPerson is < 1 or > 100)
            Errors["MaxTicketsPerPerson"] = "events.maxTicketsRange";

        // ---- Seanslar ----
        // Bu etkinliğe ait olmayan kimlikler yok sayılır; kaldırılan seansın satırı formda zaten yoktur.
        RemovedSessionIds = RemovedSessionIds.Where(id => existing.Any(x => x.Id == id)).Distinct().ToList();
        SessionRows = SessionRows.Where(r => !r.IsBlank && (r.Id is null || (existing.Any(x => x.Id == r.Id) && !RemovedSessionIds.Contains(r.Id.Value)))).ToList();
        var times = new List<(SessionInput Row, SessionTimes Times)>();
        for (var i = 0; i < SessionRows.Count; i++)
        {
            var (parsed, error) = ParseSession(SessionRows[i]);
            if (parsed is not null)
                times.Add((SessionRows[i], parsed));
            else
                Errors[$"Session.{i}"] = error!;
        }
        var keepsExisting = existing.Any(x => !RemovedSessionIds.Contains(x.Id));
        if (ev is not null && ev.HallId != Input.HallId && keepsExisting)
            Errors["HallId"] = "events.hallLocked";   // kalan seanslar eski salonun kroki sürümüne bağlı

        // Yeni seans salonun güncel kroki sürümüne bağlanır; krokisi olmayan salona seans eklenemez.
        int? planVersionId = null;
        if (SessionRows.Any(r => r.Id is null) && !Errors.ContainsKey("HallId"))
        {
            planVersionId = await db.HallPlanVersions
                .Where(v => v.HallId == Input.HallId)
                .OrderByDescending(v => v.Version)
                .Select(v => (int?)v.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (planVersionId is null)
                Errors["Sessions"] = "events.hallHasNoPlan";
        }
        // Kayıtlı seanslar yalnızca açıkça kaldırılınca silinir; bu yüzden formda satırı olmasa da kalır.
        if (Input.Status is EventStatus.Published or EventStatus.SalesClosed && !keepsExisting && !SessionRows.Any(r => r.Id is null))
            Errors["Status"] = "events.publishNeedsSession";

        string? newPoster = null;
        if (Errors.Count == 0 && Input.Poster is { Length: > 0 } file)
        {
            (newPoster, var posterError) = await posters.SaveAsync(file, cancellationToken);
            if (posterError is not null)
                Errors["Poster"] = posterError;
        }
        if (Errors.Count > 0)
            return Page();

        // ---- Veritabanına yazma (tek SaveChanges: etkinlik ve seanslar birlikte kaydedilir ya da hiçbiri) ----
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

        // Not: bilet satışı eklendiğinde satışı olan seans silinemeyecek; o zaman burada denetlenecek.
        db.EventSessions.RemoveRange(existing.Where(x => RemovedSessionIds.Contains(x.Id)));
        foreach (var (row, t) in times)
        {
            var session = row.Id is { } id ? existing.Single(x => x.Id == id) : new EventSession { HallPlanVersionId = planVersionId!.Value };
            if (row.Id is null)
                ev.Sessions.Add(session);
            (session.StartsAtUtc, session.EndsAtUtc, session.DoorsOpenAtUtc) = (t.StartUtc, t.EndUtc, t.DoorsUtc);
        }
        await db.SaveChangesAsync(cancellationToken);
        if (oldPoster != ev.PosterFileName)
            posters.Delete(oldPoster);   // eski afiş ancak kayıt başarılı olunca silinir

        Flash = "events.saved";
        return Redirect("/yonetim/etkinlikler");
    }

    // ---- Yardımcılar ----

    private Task<Event?> FindAsync(CancellationToken cancellationToken) =>
        db.Events
            .Include(e => e.Sessions).ThenInclude(x => x.HallPlanVersion)
            .FirstOrDefaultAsync(e => e.Id == Id && e.OrganizationId == Organization.DefaultId, cancellationToken);

    /// <summary>Kayıtlı seansı form satırına çevirir (Türkiye saatiyle).</summary>
    private static SessionInput ToInput(EventSession session)
    {
        var start = DisplayTime.ToLocal(session.StartsAtUtc);
        return new SessionInput
        {
            Id = session.Id,
            Date = DateOnly.FromDateTime(start),
            Start = TimeOnly.FromDateTime(start),
            End = session.EndsAtUtc is { } e ? TimeOnly.FromDateTime(DisplayTime.ToLocal(e)) : null,
            DoorsOpen = session.DoorsOpenAtUtc is { } d ? TimeOnly.FromDateTime(DisplayTime.ToLocal(d)) : null,
        };
    }

    /// <summary>Bir seans satırının UTC saatleri.</summary>
    private sealed record SessionTimes(DateTime StartUtc, DateTime? EndUtc, DateTime? DoorsUtc);

    /// <summary>
    /// Seans satırını denetleyip UTC saatlerine çevirir; hatalıysa Error dil anahtarını taşır.
    /// Bitiş başlangıçtan önceyse ertesi gün sayılır (ör. 22:00 – 01:00); kapı açılış başlangıçtan sonra olamaz.
    /// </summary>
    private static (SessionTimes? Times, string? Error) ParseSession(SessionInput row)
    {
        if (row.Date is not { } date || row.Start is not { } startTime)
            return (null, "events.sessionStartRequired");
        var start = date.ToDateTime(startTime);
        DateTime? end = row.End is { } e ? date.ToDateTime(e) : null;
        if (end <= start)
            end = end.Value.AddDays(1);
        DateTime? doors = row.DoorsOpen is { } d ? date.ToDateTime(d) : null;
        if (doors > start)
            return (null, "events.doorsAfterStart");
        return (new SessionTimes(
            DisplayTime.ToUtc(start),
            end is { } el ? DisplayTime.ToUtc(el) : null,
            doors is { } dl ? DisplayTime.ToUtc(dl) : null), null);
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

    private static string? Clean(string? value, int max)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text[..Math.Min(text.Length, max)];
    }
}
