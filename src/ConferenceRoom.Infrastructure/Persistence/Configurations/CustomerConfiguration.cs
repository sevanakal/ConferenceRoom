using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceRoom.Infrastructure.Persistence.Configurations;

/// <summary>Customers tablosu: bilet alan izleyici hesapları.</summary>
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.Property(c => c.Email).HasMaxLength(254).IsRequired();
        builder.Property(c => c.FullName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Property(c => c.PasswordHash).HasMaxLength(200).IsRequired();
        builder.HasIndex(c => new { c.OrganizationId, c.Email }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
