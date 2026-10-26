using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceRoom.Infrastructure.Persistence.Configurations;

/// <summary>SmtpSettings tablosu: kurum başına tek kayıt (OrganizationId benzersiz).</summary>
internal sealed class SmtpSettingsConfiguration : IEntityTypeConfiguration<SmtpSettings>
{
    public void Configure(EntityTypeBuilder<SmtpSettings> builder)
    {
        builder.ToTable("SmtpSettings");
        builder.Property(s => s.Host).HasMaxLength(SmtpSettings.MaxHost).IsRequired();
        builder.Property(s => s.UserName).HasMaxLength(SmtpSettings.MaxUserName);
        builder.Property(s => s.ProtectedPassword).HasMaxLength(SmtpSettings.MaxProtected);
        builder.Property(s => s.FromEmail).HasMaxLength(SmtpSettings.MaxEmail).IsRequired();
        builder.Property(s => s.FromName).HasMaxLength(SmtpSettings.MaxName);
        builder.Property(s => s.PublicUrl).HasMaxLength(SmtpSettings.MaxUrl).IsRequired();
        builder.HasIndex(s => s.OrganizationId).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Accounts.StaffUser>().WithMany().HasForeignKey(s => s.UpdatedByStaffUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
