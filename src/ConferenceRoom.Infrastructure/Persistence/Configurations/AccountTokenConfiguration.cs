using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceRoom.Infrastructure.Persistence.Configurations;

/// <summary>
/// AccountTokens tablosu: şifre yenileme ve giriş kodları. Hesap iki tablodan birinde olabildiği için (personel / izleyici)
/// yabancı anahtar yoktur; hesap türü + kimliğiyle aranır. Şifre yenileme bağlantısı kod özetiyle bulunur.
/// </summary>
internal sealed class AccountTokenConfiguration : IEntityTypeConfiguration<AccountToken>
{
    public void Configure(EntityTypeBuilder<AccountToken> builder)
    {
        builder.ToTable("AccountTokens");
        builder.Property(t => t.CodeHash).HasMaxLength(AccountToken.HashLength).IsFixedLength().IsRequired();
        builder.HasIndex(t => new { t.AccountKind, t.AccountId, t.Purpose });
        builder.HasIndex(t => t.CodeHash);
        builder.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
