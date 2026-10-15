using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceRoom.Infrastructure.Persistence.Configurations;

/// <summary>StaffUsers (yönetim kullanıcıları) ve StaffUserRoles (kullanıcı-rol bağı) tabloları.</summary>
internal sealed class StaffUserConfiguration : IEntityTypeConfiguration<StaffUser>
{
    public void Configure(EntityTypeBuilder<StaffUser> builder)
    {
        builder.ToTable("StaffUsers");
        builder.Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
        builder.HasIndex(u => new { u.OrganizationId, u.Email }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(u => u.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(u => u.Roles).WithOne().HasForeignKey(r => r.StaffUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StaffUserRoleConfiguration : IEntityTypeConfiguration<StaffUserRole>
{
    public void Configure(EntityTypeBuilder<StaffUserRole> builder)
    {
        builder.ToTable("StaffUserRoles");
        builder.HasKey(r => new { r.StaffUserId, r.RoleId });
        builder.HasOne(r => r.Role).WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}
