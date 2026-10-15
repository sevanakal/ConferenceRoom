using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConferenceRoom.Infrastructure.Persistence;

/// <summary>
/// Bekleyen migration'ları veritabanına uygular. Veritabanı yoksa oluşturur.
/// Docker kurulumunda uygulama açılırken çağrılır (Database:MigrateOnStartup = true).
/// </summary>
public static class DatabaseMigrator
{
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
