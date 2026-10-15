using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Database;

/// <summary>Migration'ların gerçek MySQL üzerinde baştan sona uygulanabildiğini doğrular.</summary>
[Collection(MySqlCollection.Name)]
public class MigrationTests
{
    [MySqlFact]
    public async Task Migrationlar_bos_veritabanina_uygulanir_ve_varsayilan_kurum_olusur()
    {
        var server = Environment.GetEnvironmentVariable(MySqlFactAttribute.EnvironmentVariable)!;
        var database = $"conferenceroom_test_{Guid.NewGuid():N}"[..40];
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL($"{server.TrimEnd(';')};Database={database}")
            .Options;

        await using var db = new AppDbContext(options);
        try
        {
            await db.Database.MigrateAsync();

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var organization = await db.Organizations.SingleAsync();
            Assert.Equal(Organization.DefaultId, organization.Id);
            Assert.Equal("Varsayılan Kurum", organization.Name);   // Türkçe karakterler bozulmadan saklanır
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
