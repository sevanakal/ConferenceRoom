using System.Globalization;
using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Localization;
using ConferenceRoom.Web.Auth;
using ConferenceRoom.Web.Common;
using ConferenceRoom.Web.Halls;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Web.Pages.Yonetim.Salonlar;

/// <summary>
/// Kroki editörü (/yonetim/salonlar/{id}/kroki). Varsayılan olarak güncel sürüm açılır; ?surum=N ile eski bir sürüm açılabilir
/// (kaydedilince yeni sürüm olur, eski sürüm değişmez). Editörün kendisi wwwroot/js/kroki-editor.js dosyasındadır.
/// Kaydet düğmesi bu sayfanın "Save" işleyicisine kroki JSON'unu gönderir (OnPostSaveAsync).
/// </summary>
public class KrokiModel(AppDbContext db, LanguageCatalog catalog, IStringLocalizer localizer, IAntiforgery antiforgery) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    /// <summary>Açılmak istenen eski sürüm (?surum=3). Boşsa güncel sürüm.</summary>
    [BindProperty(SupportsGet = true, Name = "surum")]
    public int? RequestedVersion { get; set; }

    public string HallName { get; private set; } = "";

    /// <summary>Salonun güncel sürüm numarası (hiç kayıt yoksa 0). Editör kaydederken bunu temel aldığını bildirir.</summary>
    public int LatestVersion { get; private set; }

    /// <summary>Eski bir sürüm açıldıysa onun numarası; güncel sürüm açıldıysa null.</summary>
    public int? OpenedVersion { get; private set; }

    /// <summary>Editöre verilecek kroki JSON'u.</summary>
    public string PlanJson { get; private set; } = "";

    /// <summary>Editörün ayarları ve metinleri (JSON). Sayfada &lt;script type="application/json"&gt; içinde durur.</summary>
    public object EditorConfig { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var hall = await FindAsync(cancellationToken);
        if (hall is null)
            return NotFound();

        HallName = hall.Name;
        LatestVersion = await HallPlanStore.LatestVersionAsync(db, hall.Id, cancellationToken);
        var opened = RequestedVersion is { } requested && requested != LatestVersion ? requested : LatestVersion;
        if (opened > 0)
        {
            var json = await db.HallPlanVersions
                .Where(v => v.HallId == hall.Id && v.Version == opened)
                .Select(v => v.PlanJson)
                .FirstOrDefaultAsync(cancellationToken);
            if (json is null)
                return NotFound();
            PlanJson = json;
            OpenedVersion = opened == LatestVersion ? null : opened;
        }
        else
        {
            PlanJson = HallPlan.Blank(hall.Name, localizer["planEditor.defaultLevel"]);
        }

        // Editör metinleri: dil dosyalarının "planEditor" bölümü, önek atılarak ("planEditor.tool.select" → "tool.select").
        var texts = ScreenData.Texts(catalog, "planEditor");
        EditorConfig = new
        {
            hallName = hall.Name,
            version = LatestVersion,
            openedVersion = OpenedVersion,
            culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            saveUrl = $"/yonetim/salonlar/{hall.Id}/kroki?handler=Save",
            token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken,
            texts,
        };
        return Page();
    }

    /// <summary>
    /// Krokiyi yeni sürüm olarak kaydeder. Gövde: kroki JSON'u; ?baseVersion=N: editörün temel aldığı sürüm.
    /// Cevap: { status: saved | unchanged | conflict | invalid, version, capacity, message? }
    /// </summary>
    public async Task<IActionResult> OnPostSaveAsync(int baseVersion, CancellationToken cancellationToken)
    {
        var hall = await FindAsync(cancellationToken);
        if (hall is null)
            return NotFound();

        if (Request.ContentLength > HallPlan.MaxBytes)
            return Answer(new HallPlanStore.SaveResult(HallPlanStore.Outcome.Invalid, baseVersion, 0, "hallPlan.tooLarge"));
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken);

        return Answer(await HallPlanStore.SaveAsync(db, hall, json, baseVersion, User.UserId(), cancellationToken));
    }

    // ---- Yardımcılar ----

    private Task<Hall?> FindAsync(CancellationToken cancellationToken) =>
        db.Halls.FirstOrDefaultAsync(h => h.Id == Id && h.OrganizationId == Organization.DefaultId, cancellationToken);

    private JsonResult Answer(HallPlanStore.SaveResult result) => new(new
    {
        status = result.Outcome.ToString().ToLowerInvariant(),
        version = result.Version,
        capacity = result.Capacity,
        message = result.ErrorKey is null ? null : localizer[result.ErrorKey].Value,
    })
    {
        StatusCode = result.Outcome switch
        {
            HallPlanStore.Outcome.Conflict => StatusCodes.Status409Conflict,
            HallPlanStore.Outcome.Invalid => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status200OK,
        },
    };
}
