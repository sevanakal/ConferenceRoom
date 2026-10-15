using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceRoom.Infrastructure.Persistence.Configurations;

/// <summary>
/// Roles ve RolePermissions tabloları, kurulumda gelen başlangıç rolleri.
/// Başlangıç rolleri yalnızca bir örnektir; yönetici yenilerini arayüzden ekleyebilecek.
/// </summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.HasIndex(r => new { r.OrganizationId, r.Name }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(r => r.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);

        // ---- Başlangıç verisi (tarih sabit; değişirse EF her seferinde yeni migration üretir) ----
        var created = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            new Role { Id = Role.AdministratorId, OrganizationId = Organization.DefaultId, Name = "Yönetici", Description = "Tüm yetkiler", IsSystem = true, CreatedAtUtc = created },
            new Role { Id = Role.HallManagerId, OrganizationId = Organization.DefaultId, Name = "Salon sorumlusu", Description = "Salon krokileri ve etkinlikler", CreatedAtUtc = created },
            new Role { Id = Role.SalesManagerId, OrganizationId = Organization.DefaultId, Name = "Satış sorumlusu", Description = "Bilet fiyatları, kategoriler ve satışlar", CreatedAtUtc = created });
    }
}

/// <summary>Rol-yetki eşleşmeleri. Birincil anahtar (RoleId, Permission) ikilisidir.</summary>
internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(p => new { p.RoleId, p.Permission });
        builder.Property(p => p.Permission).HasMaxLength(100);

        // Yönetici her yetkiye sahip; diğer iki rol örnek olarak işi bölüşür.
        builder.HasData(
            Permissions.All.Select(p => new RolePermission { RoleId = Role.AdministratorId, Permission = p })
                .Concat([
                    new RolePermission { RoleId = Role.HallManagerId, Permission = Permissions.HallsManage },
                    new RolePermission { RoleId = Role.HallManagerId, Permission = Permissions.EventsManage },
                    new RolePermission { RoleId = Role.SalesManagerId, Permission = Permissions.PricingManage },
                    new RolePermission { RoleId = Role.SalesManagerId, Permission = Permissions.SalesView },
                ]));
    }
}
