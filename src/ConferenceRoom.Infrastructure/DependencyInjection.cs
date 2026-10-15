using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ConferenceRoom.Infrastructure;

/// <summary>Altyapı servislerini (veritabanı bağlamı vb.) uygulamaya ekleyen tek giriş noktası.</summary>
public static class DependencyInjection
{
    /// <summary>appsettings içindeki "ConnectionStrings:Default" anahtarı.</summary>
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Veritabanı bağlantı metni bulunamadı. appsettings.json içinde ConnectionStrings:{ConnectionStringName} tanımlayın.");

        services.AddDbContext<AppDbContext>(options => options.UseMySQL(connectionString));
        return services;
    }
}
