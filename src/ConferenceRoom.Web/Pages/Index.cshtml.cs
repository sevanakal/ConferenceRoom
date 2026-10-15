using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Pages;

/// <summary>
/// Ana sayfa. Şimdilik yalnızca sistemin ayakta olduğunu gösterir: veritabanına bağlanılabiliyor mu,
/// varsayılan kurum oluşmuş mu. Uygulama büyüdükçe burası panoya dönüşecek.
/// </summary>
public class IndexModel(AppDbContext db, ILogger<IndexModel> logger) : PageModel
{
    public bool DatabaseConnected { get; private set; }

    public string? OrganizationName { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            OrganizationName = await db.Organizations
                .Where(o => o.Id == Organization.DefaultId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(cancellationToken);
            DatabaseConnected = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Veritabanı yoksa sayfa yine açılır; durum kartı sorunu gösterir.
            logger.LogWarning(ex, "Veritabanına bağlanılamadı.");
        }
    }
}
