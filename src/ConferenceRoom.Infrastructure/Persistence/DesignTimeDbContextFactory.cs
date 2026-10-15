using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ConferenceRoom.Infrastructure.Persistence;

/// <summary>
/// Yalnızca "dotnet ef migrations add ..." gibi komutlar için kullanılır; uygulama çalışırken devreye girmez.
/// Migration üretmek veritabanına bağlanmayı gerektirmez, bu yüzden buradaki bağlantı metni yalnızca biçim içindir.
/// Komut örneği (src klasöründe):
///   dotnet ef migrations add MigrationAdi -p ConferenceRoom.Infrastructure -o Persistence/Migrations
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("Server=localhost;Port=3306;Database=conferenceroom;User=konferans;Password=gelistirme")
            .Options;
        return new AppDbContext(options);
    }
}
