using ConferenceRoom.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceRoom.Infrastructure.Persistence.Configurations;

/// <summary>Organizations tablosu ve kurulumda oluşturulan varsayılan kurum.</summary>
internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Slug).HasMaxLength(100).IsRequired();
        builder.HasIndex(o => o.Slug).IsUnique();

        // Başlangıç verisi: tek kurumlu kurulumda kullanılan varsayılan kurum.
        // Tarih sabit yazılır; değişirse EF her seferinde yeni migration üretir.
        builder.HasData(new Organization
        {
            Id = Organization.DefaultId,
            Name = "Varsayılan Kurum",
            Slug = "varsayilan",
            IsActive = true,
            CreatedAtUtc = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
