using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Tests.Database;

namespace ConferenceRoom.Tests.Auth;

/// <summary>
/// SMTP ayarları hiç yapılmamışken (gerçek MySQL; bu sınıfın veritabanında ayar yazılmaz): şifremi unuttum sayfası
/// e-posta gönderilemeyeceğini söyler, iki adımlı doğrulama açılamaz (açılsaydı kullanıcı giremezdi).
/// </summary>
[Collection(MySqlCollection.Name)]
public class EmailNotConfiguredTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Ayar_yokken_sifremi_unuttum_yoneticiye_yonlendirir()
    {
        var html = await Forms.PostAsync(factory.CreateClient(), "/sifremi-unuttum", [new("Email", MySqlWebAppFactory.AdminEmail)]);
        Assert.Contains("E-posta gönderimi henüz ayarlanmamış.", html);
    }

    [MySqlFact]
    public async Task Ayar_yokken_iki_adimli_dogrulama_acilamaz()
    {
        var (email, id) = await factory.CreateStaffAsync([Permissions.HallsManage]);
        var admin = await factory.SignInStaffAsync();
        var html = await Forms.PostAsync(admin, $"/yonetim/kullanicilar/duzenle/{id}",
        [
            new("Input.FullName", "Satış personeli"), new("Input.Email", email), new("Input.IsActive", "true"),
            new("Input.TwoFactorEnabled", "true"), new("Input.TwoFactorEnabled", "false"),
        ]);
        Assert.Contains("İki adımlı doğrulama için önce e-posta (SMTP) ayarlarını yapın.", html);
    }
}
