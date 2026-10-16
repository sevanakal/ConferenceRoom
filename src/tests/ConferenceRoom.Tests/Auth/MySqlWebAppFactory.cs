using ConferenceRoom.Infrastructure.Persistence;
using ConferenceRoom.Tests.Database;
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
