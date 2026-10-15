namespace ConferenceRoom.Tests.Database;

/// <summary>
/// Gerçek MySQL sunucusu gerektiren testler. CONFERENCEROOM_TEST_MYSQL ortam değişkeni bir bağlantı metni
/// içermiyorsa test atlanır (ör. "Server=localhost;Port=3306;User=root;Password=...").
/// Testler kendi geçici veritabanlarını oluşturup sonunda siler.
/// </summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "CONFERENCEROOM_TEST_MYSQL";

    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
            Skip = $"{EnvironmentVariable} tanımlı değil; MySQL testi atlandı.";
    }
}
