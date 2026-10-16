using System.Globalization;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Domain.Sales;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Localization;
using ConferenceRoom.Web.Auth;
using ConferenceRoom.Web.Common;
using ConferenceRoom.Web.Sales;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Web.Pages.Yonetim.SatisYetkileri;

/// <summary>
/// Koltuk kapsamı ekranı: koltuk kısıtlaması olan personele krokiden koltuk verilir.
///   /yonetim/satis-yetkileri/salon/{id}     salonun genel kaydı (salondaki tüm etkinlikler)
///   /yonetim/satis-yetkileri/etkinlik/{id}  yalnızca bu etkinliğe özel kayıt (varsa salon kaydının yerine geçer)
/// Ekranın kendisi wwwroot/js/satis-yetkileri.js; kroki, seçim ve kaydetme koltuk ayarlarıyla ortaktır
/// (koltuk-secici.js, _SeatScreenLayout). "users.manage" yetkisi ister (Program.cs).
/// </summary>
public class KoltuklarModel(AppDbContext db, LanguageCatalog catalog, IStringLocalizer localizer, IAntiforgery antiforgery) : PageModel
{
    public const string HallKind = "salon", EventKind = "etkinlik";

    /// <summary>"salon" ya da "etkinlik".</summary>
    [BindProperty(SupportsGet = true)]
    public string Kind { get; set; } = HallKind;

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public string Title { get; private set; } = "";

    public string HallName { get; private set; } = "";

    public bool IsEvent => Kind == EventKind;

    /// <summary>Etkinlik ekranında etkinliğe özel kayıt var mı (yoksa salonun kaydı gösterilir).</summary>
    public bool IsEventSpecific { get; private set; }

    /// <summary>Gösterilecek kroki; salonun krokisi yoksa null.</summary>
    public string? PlanJson { get; private set; }

    public string ScopeJson { get; private set; } = "";

    public object EditorConfig { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var (hall, ev) = await FindAsync(cancellationToken);
        if (hall is null)
            return NotFound();

        HallName = hall.Name;
        Title = ev?.Title ?? hall.Name;
        var snapshot = await SeatScopeStore.LoadAsync(db, hall.Id, ev?.Id, cancellationToken);
        PlanJson = snapshot.PlanJson;
        ScopeJson = snapshot.ScopeJson;
        IsEventSpecific = snapshot.IsEventSpecific;

        var sellers = await SeatScopeStore.SellersAsync(db, hall.OrganizationId, cancellationToken);
        EditorConfig = new
        {
            revision = snapshot.Revision,
            culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            saveUrl = $"/yonetim/satis-yetkileri/{Kind}/{Id}?handler=Save",
            token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken,
            staff = sellers.Select(s => new { id = s.Id, name = s.FullName, restricted = s.SeatsRestricted }),
            // Ortak metinler "seatEditor", bu ekrana özgü olanlar "seatScope" bölümünden.
            texts = ScreenData.Texts(catalog, "seatEditor", "seatScope"),
        };
        return Page();
    }

    /// <summary>
    /// Kapsamı kaydeder. Gövde: kapsam JSON'u (<see cref="SeatScopeData"/>); ?baseRevision=N: ekranın dayandığı kayıt.
    /// Cevap: { status: saved | unchanged | conflict | invalid, revision, message? }
    /// </summary>
    public async Task<IActionResult> OnPostSaveAsync(int baseRevision, CancellationToken cancellationToken)
    {
        var (hall, ev) = await FindAsync(cancellationToken);
        if (hall is null)
            return NotFound();

        SeatScopeStore.SaveResult result;
        if (Request.ContentLength > SeatScopeData.MaxBytes)
        {
            result = new SeatScopeStore.SaveResult(SeatScopeStore.Outcome.Invalid, baseRevision, "seatScope.tooLarge");
        }
        else
        {
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync(cancellationToken);
            result = await SeatScopeStore.SaveAsync(db, hall, ev?.Id, json, baseRevision, User.UserId(), cancellationToken);
        }

        return new JsonResult(new
        {
            status = result.Outcome.ToString().ToLowerInvariant(),
            revision = result.Revision,
            message = result.ErrorKey is null ? null : localizer[result.ErrorKey].Value,
        })
        {
            StatusCode = result.Outcome switch
            {
                SeatScopeStore.Outcome.Conflict => StatusCodes.Status409Conflict,
                SeatScopeStore.Outcome.Invalid => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status200OK,
            },
        };
    }

    /// <summary>Etkinliğe özel kaydı siler; etkinlik yeniden salonun genel kaydını kullanır.</summary>
    public async Task<IActionResult> OnPostResetAsync(CancellationToken cancellationToken)
    {
        var (hall, ev) = await FindAsync(cancellationToken);
        if (hall is null || ev is null)
            return NotFound();
        await SeatScopeStore.ResetEventAsync(db, hall.Id, ev.Id, cancellationToken);
        return Redirect($"/yonetim/satis-yetkileri/{EventKind}/{ev.Id}");
    }

    /// <summary>Adresteki salon ya da etkinlik (etkinlikte salonu da). Bulunamazsa salon null.</summary>
    private async Task<(Hall? Hall, Event? Event)> FindAsync(CancellationToken cancellationToken)
    {
        if (Kind == HallKind)
            return (await db.Halls.FirstOrDefaultAsync(h => h.Id == Id && h.OrganizationId == Organization.DefaultId, cancellationToken), null);
        if (Kind != EventKind)
            return (null, null);
        var ev = await db.Events.Include(e => e.Hall).FirstOrDefaultAsync(e => e.Id == Id && e.OrganizationId == Organization.DefaultId, cancellationToken);
        return (ev?.Hall, ev);
    }
}
