using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceRoom.Infrastructure.Persistence.Configurations;

/// <summary>
/// SeatScopes tablosu: personelin satabileceği koltuklar, salon başına bir genel kayıt ve gerekirse etkinliğe özel kayıtlar.
/// </summary>
internal sealed class SeatScopeConfiguration : IEntityTypeConfiguration<SeatScope>
{
    public void Configure(EntityTypeBuilder<SeatScope> builder)
    {
        builder.ToTable("SeatScopes");
        builder.Property(s => s.ScopeJson).HasColumnType("longtext").IsRequired();
        builder.Property(s => s.Revision).IsConcurrencyToken();   // aynı anda iki kayıt: ikincisi DbUpdateConcurrencyException alır

        // Salon + etkinlik başına bir kayıt. MySQL benzersiz indekste NULL'ları farklı saydığı için salonun genel kaydı
        // (EventId boş) indekse EventKey = COALESCE(EventId, 0) hesaplanan sütunuyla girer.
        builder.Property<int>("EventKey").HasComputedColumnSql("COALESCE(`EventId`, 0)", stored: true);
        builder.HasIndex("HallId", "EventKey").IsUnique();

        builder.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Hall).WithMany().HasForeignKey(s => s.HallId).OnDelete(DeleteBehavior.Restrict);
        // Etkinlikler silinmez (iptal edilir); MySQL hesaplanan sütunun kaynağındaki yabancı anahtarda CASCADE kabul etmez.
        builder.HasOne(s => s.Event).WithMany().HasForeignKey(s => s.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.UpdatedBy).WithMany().HasForeignKey(s => s.UpdatedByStaffUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
