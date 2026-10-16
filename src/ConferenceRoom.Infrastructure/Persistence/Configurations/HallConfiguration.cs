using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceRoom.Infrastructure.Persistence.Configurations;

/// <summary>Halls (salonlar) ve HallPlanVersions (kroki sürümleri) tabloları.</summary>
internal sealed class HallConfiguration : IEntityTypeConfiguration<Hall>
{
    public void Configure(EntityTypeBuilder<Hall> builder)
    {
        builder.ToTable("Halls");
        builder.Property(h => h.Name).HasMaxLength(200).IsRequired();
        builder.Property(h => h.Description).HasMaxLength(1000);
        builder.HasIndex(h => new { h.OrganizationId, h.Name }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(h => h.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(h => h.PlanVersions).WithOne().HasForeignKey(v => v.HallId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class HallPlanVersionConfiguration : IEntityTypeConfiguration<HallPlanVersion>
{
    public void Configure(EntityTypeBuilder<HallPlanVersion> builder)
    {
        builder.ToTable("HallPlanVersions");
        // Aynı salona aynı anda iki kayıt gelirse ikincisi bu indekse takılır (sürüm numarası çakışmaz).
        builder.HasIndex(v => new { v.HallId, v.Version }).IsUnique();
        builder.Property(v => v.PlanJson).HasColumnType("longtext").IsRequired();
        builder.HasOne(v => v.CreatedBy).WithMany().HasForeignKey(v => v.CreatedByStaffUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
