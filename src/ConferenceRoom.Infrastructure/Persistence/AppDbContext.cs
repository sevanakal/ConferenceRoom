using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Domain.Sales;
using ConferenceRoom.Domain.Settings;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Infrastructure.Persistence;

/// <summary>
/// Uygulamanın veritabanı bağlamı. Her tablo burada bir DbSet olarak görünür;
/// tablo ayrıntıları (sütun uzunlukları, indeksler, başlangıç verisi) Configurations
/// klasöründeki ayrı sınıflardadır, böylece bu dosya kısa kalır.
/// Uygulamanın şifreleme anahtarları (çerezler, SMTP şifresi) da burada saklanır (<see cref="IDataProtectionKeyContext"/>):
/// sunucu ya da Docker kapsayıcısı yeniden başlayınca anahtarlar kaybolmaz.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Organization> Organizations => Set<Organization>();

    // ---- Hesaplar: yönetim kullanıcıları, roller ve bilet alan izleyiciler ----
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    public DbSet<StaffUserRole> StaffUserRoles => Set<StaffUserRole>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();

    // ---- Salonlar ve kroki sürümleri ----
    public DbSet<Hall> Halls => Set<Hall>();
    public DbSet<HallPlanVersion> HallPlanVersions => Set<HallPlanVersion>();

    // ---- Etkinlikler, türleri ve seansları ----
    public DbSet<EventType> EventTypes => Set<EventType>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventSession> EventSessions => Set<EventSession>();
    public DbSet<EventSeatMap> EventSeatMaps => Set<EventSeatMap>();

    // ---- Satış: personelin satabileceği koltuklar, satış/rezervasyon işlemleri, biletler ve bildirimler ----
    public DbSet<SeatScope> SeatScopes => Set<SeatScope>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Notification> Notifications => Set<Notification>();

    // ---- Ayarlar ve uygulamanın şifreleme anahtarları ----
    public DbSet<SmtpSettings> SmtpSettings => Set<SmtpSettings>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configurations klasöründeki tüm IEntityTypeConfiguration sınıflarını uygula.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
