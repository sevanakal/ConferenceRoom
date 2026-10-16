using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceRoom.Infrastructure.Persistence.Configurations;

/// <summary>
/// EventTypes (etkinlik türleri), Events (etkinlikler) ve EventSessions (seanslar) tabloları.
/// Durum ve satış türü veritabanında okunabilsin diye metin olarak saklanır ("Published", "Sale").
/// </summary>
internal sealed class EventTypeConfiguration : IEntityTypeConfiguration<EventType>
{
    public void Configure(EntityTypeBuilder<EventType> builder)
    {
        builder.ToTable("EventTypes");
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.DefaultSaleMode).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(t => new { t.OrganizationId, t.Name }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        // ---- Başlangıç verisi: örnek türler; yönetici arayüzden değiştirip yenilerini ekler (tarih sabit, bkz. RoleConfiguration) ----
        var created = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
        EventType Seed(int id, string name, SaleMode mode, bool approval) =>
            new() { Id = id, OrganizationId = Organization.DefaultId, Name = name, DefaultSaleMode = mode, DefaultRequiresApproval = approval, CreatedAtUtc = created };
        builder.HasData(
            Seed(EventType.ConferenceId, "Konferans", SaleMode.Reservation, false),
            Seed(EventType.ConcertId, "Konser", SaleMode.Sale, false),
            Seed(EventType.TheatreId, "Tiyatro", SaleMode.Sale, false),
            Seed(EventType.SeminarId, "Seminer", SaleMode.Reservation, true),
            Seed(EventType.MeetingId, "Toplantı", SaleMode.Reservation, true));
    }
}

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events");
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.Organizer).HasMaxLength(200);
        builder.Property(e => e.PosterFileName).HasMaxLength(100);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SaleMode).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => new { e.OrganizationId, e.Status });
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.EventType).WithMany().HasForeignKey(e => e.EventTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Hall).WithMany().HasForeignKey(e => e.HallId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.CreatedBy).WithMany().HasForeignKey(e => e.CreatedByStaffUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Sessions).WithOne().HasForeignKey(s => s.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EventSessionConfiguration : IEntityTypeConfiguration<EventSession>
{
    public void Configure(EntityTypeBuilder<EventSession> builder)
    {
        builder.ToTable("EventSessions");
        builder.HasIndex(s => s.StartsAtUtc);   // pano: yaklaşan seanslar
        builder.HasOne(s => s.HallPlanVersion).WithMany().HasForeignKey(s => s.HallPlanVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
