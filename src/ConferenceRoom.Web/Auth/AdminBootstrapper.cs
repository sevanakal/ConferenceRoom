using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Auth;

/// <summary>
/// İlk yönetici hesabı. Veritabanında hiç yönetim kullanıcısı yoksa ve ayarlarda
/// "Admin:Email" ile "Admin:Password" verilmişse bu bilgilerle Yönetici rolünde bir hesap açar.
/// Hesap bir kez oluşur; sonra ayardaki şifre değişse de mevcut hesaba dokunulmaz.
/// Geliştirmede appsettings.Development.json ve docker-compose.yml bu değerleri verir.
/// </summary>
public static class AdminBootstrapper
{
    public static async Task EnsureAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var email = configuration["Admin:Email"];
        var password = configuration["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.StaffUsers.AnyAsync(cancellationToken))
            return;

        var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();
        db.StaffUsers.Add(new StaffUser
        {
            OrganizationId = Organization.DefaultId,
            Email = PasswordService.NormalizeEmail(email),
            FullName = configuration["Admin:FullName"] ?? "Yönetici",
            PasswordHash = passwords.Hash(password),
            Roles = [new StaffUserRole { RoleId = Role.AdministratorId }],
        });
        await db.SaveChangesAsync(cancellationToken);
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AdminBootstrapper))
            .LogInformation("İlk yönetici hesabı oluşturuldu: {Email}", email);
    }
}
