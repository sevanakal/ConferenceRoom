using ConferenceRoom.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Infrastructure.Persistence;

/// <summary>
/// Uygulamanın veritabanı bağlamı. Her tablo burada bir DbSet olarak görünür;
/// tablo ayrıntıları (sütun uzunlukları, indeksler, başlangıç verisi) Configurations
/// klasöründeki ayrı sınıflardadır, böylece bu dosya kısa kalır.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configurations klasöründeki tüm IEntityTypeConfiguration sınıflarını uygula.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
