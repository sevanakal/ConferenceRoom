using ConferenceRoom.Domain.Accounts;
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

    // ---- Hesaplar: yönetim kullanıcıları, roller ve bilet alan izleyiciler ----
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    public DbSet<StaffUserRole> StaffUserRoles => Set<StaffUserRole>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configurations klasöründeki tüm IEntityTypeConfiguration sınıflarını uygula.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
