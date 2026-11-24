using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Tests.Database;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Auth;

/// <summary>
/// İki adımlı doğrulama (gerçek MySQL; kodlar TestEmailSender'dan okunur): açık olan personel şifreden sonra e-postasına
/// gelen kodu girmeden panele ya da satış bölümüne giremez; yanlış kod sayılır, 5 yanlıştan sonra yeni kod gerekir.
/// Kapalı olan personel eskisi gibi doğrudan girer.
/// </summary>
[Collection(MySqlCollection.Name)]
public class TwoFactorTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Panel_girisinde_eposta_kodu_girilmeden_oturum_acilmaz()
    {
        var email = await CreateTwoFactorStaffAsync([Permissions.HallsManage]);
        var client = factory.CreateClient();

        var html = await Forms.PostAsync(client, "/yonetim/giris", [new("Input.Email", email), new("Input.Password", MySqlWebAppFactory.StaffPassword)]);
        Assert.Contains("6 haneli bir kod gönderdik", html);
        Assert.Contains("/yonetim/giris", (await client.GetAsync("/yonetim")).RequestMessage!.RequestUri!.ToString());

        var wrong = await Forms.PostAsync(client, "/dogrulama", [new("Code", WrongCode(email))]);
        Assert.Contains("Kod hatalı.", wrong);

        var done = await Forms.PostAsync(client, "/dogrulama", [new("Code", factory.Emails.LastCode(email))]);
        Assert.Contains("Hoş geldiniz", done);
    }

    [MySqlFact]
    public async Task Satis_girisinde_de_kod_istenir_ve_dogru_kodla_satis_bolumune_girilir()
    {
        var email = await CreateTwoFactorStaffAsync([Permissions.SalesSell]);
        var client = factory.CreateClient();
        await Forms.PostAsync(client, "/giris", [new("Input.Email", email), new("Input.Password", MySqlWebAppFactory.StaffPassword)]);
        Assert.Contains("/giris", (await client.GetAsync("/satis")).RequestMessage!.RequestUri!.ToString());

        await Forms.PostAsync(client, "/dogrulama", [new("Code", factory.Emails.LastCode(email))]);
        var page = await client.GetAsync("/satis");
        Assert.EndsWith("/satis", page.RequestMessage!.RequestUri!.ToString());
    }

    [MySqlFact]
    public async Task Bes_yanlis_denemeden_sonra_dogru_kod_da_gecmez_yeni_kod_istenir()
    {
        var email = await CreateTwoFactorStaffAsync([Permissions.HallsManage]);
        var client = factory.CreateClient();
        await Forms.PostAsync(client, "/yonetim/giris", [new("Input.Email", email), new("Input.Password", MySqlWebAppFactory.StaffPassword)]);
        var code = factory.Emails.LastCode(email);
        for (var i = 0; i < 5; i++)
            await Forms.PostAsync(client, "/dogrulama", [new("Code", WrongCode(email))]);
        Assert.Contains("Yeni kod isteyin.", await Forms.PostAsync(client, "/dogrulama", [new("Code", code)]));

        // Yeni kod istenir, onunla girilir.
        Assert.Contains("Yeni kod gönderildi.", await Forms.PostAsync(client, "/dogrulama", [], postUrl: "/dogrulama?handler=Resend"));
        Assert.Contains("Hoş geldiniz", await Forms.PostAsync(client, "/dogrulama", [new("Code", factory.Emails.LastCode(email))]));
    }

    [MySqlFact]
    public async Task Yonetici_kullanici_duzenlemeden_iki_adimli_dogrulamayi_acar()
    {
        await factory.ConfigureSmtpAsync();
        var (email, id) = await factory.CreateStaffAsync([Permissions.HallsManage]);
        var admin = await factory.SignInStaffAsync();
        var html = await Forms.PostAsync(admin, $"/yonetim/kullanicilar/duzenle/{id}",
        [
            new("Input.FullName", "Satış personeli"), new("Input.Email", email), new("Input.IsActive", "true"),
            new("Input.TwoFactorEnabled", "true"), new("Input.TwoFactorEnabled", "false"),
        ]);
        Assert.Contains("Kullanıcı kaydedildi.", html);
        await factory.WithDbAsync(async db => Assert.True(await db.StaffUsers.Where(u => u.Id == id).Select(u => u.TwoFactorEnabled).SingleAsync()));
    }

    // ---- Yardımcılar ----

    private async Task<string> CreateTwoFactorStaffAsync(string[] permissions)
    {
        await factory.ConfigureSmtpAsync();
        var (email, id) = await factory.CreateStaffAsync(permissions);
        await factory.WithDbAsync(async db =>
        {
            (await db.StaffUsers.SingleAsync(u => u.Id == id)).TwoFactorEnabled = true;
            await db.SaveChangesAsync();
        });
        return email;
    }

    /// <summary>Son gönderilen koddan farklı 6 hane.</summary>
    private string WrongCode(string email) => factory.Emails.LastCode(email) == "000000" ? "111111" : "000000";
}
