using System.Globalization;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Localization;
using ConferenceRoom.Web.Auth;
using ConferenceRoom.Web.Common;
using ConferenceRoom.Web.Events;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Web.Pages.Yonetim.Fiyatlandirma;

/// <summary>
/// Etkinlik koltuk ayarları (/yonetim/fiyatlandirma/{id}): bilet kategorileri ve fiyatları, koltuklara kategori atama,
/// internetten satışa açık koltukların seçimi. Ekranın kendisi wwwroot/js/koltuk-ayarlari.js dosyasındadır; kroki,
/// seçim ve kaydetme satış yetkileri ekranıyla ortaktır (koltuk-secici.js, _SeatScreenLayout). Kaydet düğmesi ayarları "Save" işleyicisine gönderir.
/// "pricing.manage" yetkisi ister (Program.cs).
/// </summary>
public class KoltuklarModel(AppDbContext db, LanguageCatalog catalog, IStringLocalizer localizer, IAntiforgery antiforgery) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public string EventTitle { get; private set; } = "";

    public string HallName { get; private set; } = "";

    /// <summary>Gösterilecek kroki (seansların bağlı olduğu en yeni sürüm); etkinliğin seansı yoksa null.</summary>
    public string? PlanJson { get; private set; }

    public string MapJson { get; private set; } = "";

    /// <summary>Seansların bağlı olduğu farklı kroki sürümü sayısı; birden çoksa ekranda not çıkar.</summary>
    public int PlanVersionCount { get; private set; }

    /// <summary>Ekranın ayarları ve metinleri (JSON). Sayfada &lt;script type="application/json"&gt; içinde durur.</summary>
    public object EditorConfig { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var ev = await FindAsync(cancellationToken);
        if (ev is null)
            return NotFound();

        EventTitle = ev.Title;
        HallName = await db.Halls.Where(h => h.Id == ev.HallId).Select(h => h.Name).FirstAsync(cancellationToken);
        var snapshot = await SeatMapStore.LoadAsync(db, ev.Id, cancellationToken);
        PlanJson = snapshot.PlanJson;
        MapJson = snapshot.MapJson;
        PlanVersionCount = snapshot.PlanVersions.Count;

        EditorConfig = new
        {
            eventTitle = ev.Title,
            revision = snapshot.Revision,
            onlineEnabled = ev.OnlineEnabled,
            culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            saveUrl = $"/yonetim/fiyatlandirma/{ev.Id}?handler=Save",
            token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken,
            texts = ScreenData.Texts(catalog, "seatEditor"),   // "seatEditor.save" → "save"
        };
        return Page();
    }

    /// <summary>
    /// Ayarları kaydeder. Gövde: ayar JSON'u (<see cref="SeatMap"/>); ?baseRevision=N: ekranın dayandığı kayıt.
    /// Cevap: { status: saved | unchanged | conflict | invalid, revision, message? }
    /// </summary>
    public async Task<IActionResult> OnPostSaveAsync(int baseRevision, CancellationToken cancellationToken)
    {
        var ev = await FindAsync(cancellationToken);
        if (ev is null)
            return NotFound();

        SeatMapStore.SaveResult result;
        if (Request.ContentLength > SeatMap.MaxBytes)
        {
            result = new SeatMapStore.SaveResult(SeatMapStore.Outcome.Invalid, baseRevision, "seatMap.tooLarge");
        }
        else
        {
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync(cancellationToken);
            result = await SeatMapStore.SaveAsync(db, ev, json, baseRevision, User.UserId(), cancellationToken);
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
                SeatMapStore.Outcome.Conflict => StatusCodes.Status409Conflict,
                SeatMapStore.Outcome.Invalid => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status200OK,
            },
        };
    }

    private Task<Event?> FindAsync(CancellationToken cancellationToken) =>
        db.Events.FirstOrDefaultAsync(e => e.Id == Id && e.OrganizationId == Organization.DefaultId, cancellationToken);
}
