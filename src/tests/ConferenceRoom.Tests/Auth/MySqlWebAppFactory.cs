using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Tests.Database;
using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConferenceRoom.Tests.Auth;

/// <summary>
/// Uygulamayı gerçek MySQL ile bellekte başlatır: kendine ait geçici bir veritabanı açar, migration'ları
/// uygular, ilk yönetici hesabını oluşturur; testler bitince veritabanını siler.
/// Yalnızca CONFERENCEROOM_TEST_MYSQL tanımlıyken kullanılır (bkz. MySqlFactAttribute).
/// </summary>
public sealed class MySqlWebAppFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "yonetici@test.local", AdminPassword = "Test.Sifre.123";

    /// <summary><see cref="CreateStaffAsync"/> ile açılan personelin şifresi.</summary>
    public const string StaffPassword = "Satis.Sifre.1";

    private readonly string _database = $"conferenceroom_test_{Guid.NewGuid():N}"[..40];

    /// <summary>Testlerde yüklenen afişler geçici bir klasöre yazılır, test bitince silinir.</summary>
    private readonly string _posters = Path.Combine(Path.GetTempPath(), $"conferenceroom_posters_{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var server = Environment.GetEnvironmentVariable(MySqlFactAttribute.EnvironmentVariable) ?? "";
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"{server.TrimEnd(';')};Database={_database}");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Admin:Email", AdminEmail);
        builder.UseSetting("Admin:Password", AdminPassword);
        builder.UseSetting("Admin:FullName", "Test Yöneticisi");
        builder.UseSetting("Auth:LoginAttemptsPerMinute", "1000");
        builder.UseSetting("Storage:PostersPath", _posters);   // testler aynı "IP"den çok giriş yapar
    }

    /// <summary>Yönetim paneline giriş yapmış bir istemci döner (varsayılan: ilk yönetici hesabı).</summary>
    public async Task<HttpClient> SignInStaffAsync(string email = AdminEmail, string password = AdminPassword)
    {
        var client = CreateClient();
        var html = await Forms.PostAsync(client, "/yonetim/giris", [new("Input.Email", email), new("Input.Password", password)]);
        Assert.Contains("Hoş geldiniz", html);
        return client;
    }

    /// <summary>
    /// Doğrudan veritabanına verilen yetkileri taşıyan yeni bir rol ve o roldeki "Satış personeli" adlı kullanıcıyı ekler
    /// (şifre <see cref="StaffPassword"/>); (e-posta, kullanıcı no) döner.
    /// </summary>
    public async Task<(string Email, int Id)> CreateStaffAsync(string[] permissions, bool seatsRestricted = false)
    {
        var email = $"personel-{Guid.NewGuid():N}@test.local";
        var id = 0;
        await WithDbAsync(async db =>
        {
            var role = new Role
            {
                OrganizationId = Organization.DefaultId,
                Name = $"Rol {Guid.NewGuid():N}"[..20],
                Permissions = [.. permissions.Select(p => new RolePermission { Permission = p })],
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            var user = new StaffUser
            {
                OrganizationId = Organization.DefaultId,
                Email = email,
                FullName = "Satış personeli",
                PasswordHash = new PasswordService().Hash(StaffPassword),
                SeatsRestricted = seatsRestricted,
                Roles = [new StaffUserRole { RoleId = role.Id }],
            };
            db.StaffUsers.Add(user);
            await db.SaveChangesAsync();
            id = user.Id;
        });
        return (email, id);
    }

    /// <summary>Genel girişten (/giris) satış bölümüne giriş yapmış bir istemci döner (satış personeli).</summary>
    public async Task<HttpClient> SignInSellerAsync(string email = AdminEmail, string password = AdminPassword)
    {
        var client = CreateClient();
        await Forms.PostAsync(client, "/giris", [new("Input.Email", email), new("Input.Password", password)]);
        return client;
    }

    /// <summary>Testte doğrudan veritabanına kayıt eklemek için.</summary>
    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public override async ValueTask DisposeAsync()
    {
        if (Environment.GetEnvironmentVariable(MySqlFactAttribute.EnvironmentVariable) is not null)
            await WithDbAsync(db => db.Database.EnsureDeletedAsync());
        await base.DisposeAsync();
        if (Directory.Exists(_posters))
            Directory.Delete(_posters, recursive: true);
    }
}
