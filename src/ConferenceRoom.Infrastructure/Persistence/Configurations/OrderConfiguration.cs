using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceRoom.Infrastructure.Persistence.Configurations;

/// <summary>
/// Orders (satış/rezervasyon işlemleri), Tickets (işlemdeki koltuklar) ve Notifications (satış bildirimleri) tabloları. Tür, kanal ve ödeme durumu
/// veritabanında okunabilsin diye metin olarak saklanır. Aynı seansta bir koltuk bir kez satılabilir: (EventSessionId, SeatId)
/// benzersiz indeksi aynı anda yapılan iki satıştan ikincisini durdurur.
/// </summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.Property(o => o.BuyerName).HasMaxLength(Order.MaxBuyerName).IsRequired();
        builder.Property(o => o.BuyerPhone).HasMaxLength(Order.MaxBuyerPhone);
        builder.Property(o => o.Note).HasMaxLength(Order.MaxNote);
        builder.Property(o => o.Total).HasPrecision(12, 2);
        builder.Property(o => o.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Payment).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(o => new { o.EventId, o.CreatedAtUtc });
        builder.HasOne<Organization>().WithMany().HasForeignKey(o => o.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Event).WithMany().HasForeignKey(o => o.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.EventSession).WithMany().HasForeignKey(o => o.EventSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.SoldBy).WithMany().HasForeignKey(o => o.SoldByStaffUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.Tickets).WithOne().HasForeignKey(t => t.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");
        builder.Property(t => t.SeatId).HasMaxLength(Ticket.MaxSeatId).IsRequired();
        builder.Property(t => t.SeatLabel).HasMaxLength(Ticket.MaxSeatLabel).IsRequired();
        builder.Property(t => t.CategoryName).HasMaxLength(Ticket.MaxCategoryName).IsRequired();
        builder.Property(t => t.Price).HasPrecision(12, 2);
        builder.HasIndex(t => new { t.EventSessionId, t.SeatId }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Events.EventSession>().WithMany().HasForeignKey(t => t.EventSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Notifications tablosu: kullanıcı başına satış bildirimleri; okunmamışlar kullanıcıya göre hızlı sayılır.</summary>
internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasIndex(n => new { n.StaffUserId, n.ReadAtUtc });
        builder.HasOne<Organization>().WithMany().HasForeignKey(n => n.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Accounts.StaffUser>().WithMany().HasForeignKey(n => n.StaffUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(n => n.Order).WithMany().HasForeignKey(n => n.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
