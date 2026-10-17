using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Tests.Database;
using ConferenceRoom.Web.Auth;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Auth;

/// <summary>
/// Satış personelinin girişi (gerçek MySQL): satış yetkisi olan personel genel giriş sayfasından (/giris) satış bölümüne
/// (/satis) girer; bu oturumla yönetim paneli açılmaz. Yönetim girişi yalnızca panel yetkisi olanlara açıktır.
/// </summary>
[Collection(MySqlCollection.Name)]
public class SalesLoginTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Yalnizca_satis_yapan_personel_genel_giristen_satis_bolumune_girer_panele_giremez()
    {
        var email = await CreateSellerAsync([Permissions.SalesSell]);
        var client = factory.CreateClient();

        var html = await Forms.PostAsync(client, "/giris", Login(email));

        Assert.Contains("Satış personeli", html);   // /satis açılış sayfası, adıyla
        var panel = await client.GetAsync("/yonetim");
        Assert.Contains("/yonetim/giris", panel.RequestMessage!.RequestUri!.ToString());
    }

    [MySqlFact]
    public async Task Yalnizca_satis_yapan_personel_yonetim_girisinden_giremez()
    {
        var email = await CreateSellerAsync([Permissions.SalesSell]);
        var client = factory.CreateClient();

        var html = await Forms.PostAsync(client, "/yonetim/giris", Login(email));

        Assert.Contains("yönetim paneline giriş yetkisi yok", html);
        var panel = await client.GetAsync("/yonetim");
        Assert.Contains("/yonetim/giris", panel.RequestMessage!.RequestUri!.ToString());
    }

    [MySqlFact]
    public async Task Satis_yetkisi_olmayan_personel_genel_giristen_giremez()
    {
        var email = await CreateSellerAsync([Permissions.HallsManage]);
        var client = factory.CreateClient();

        var html = await Forms.PostAsync(client, "/giris", Login(email));

        Assert.Contains("E-posta ya da şifre hatalı.", html);
        var sales = await client.GetAsync("/satis");
        Assert.Contains("/giris", sales.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [MySqlFact]
    public async Task Pasif_yapilan_satis_personelinin_oturumu_hemen_kapanir()
    {
        var email = await CreateSellerAsync([Permissions.SalesSell]);
        var client = factory.CreateClient();
        await Forms.PostAsync(client, "/giris", Login(email));

        await factory.WithDbAsync(async db =>
        {
            var user = await db.StaffUsers.SingleAsync(u => u.Email == email);
            user.IsActive = false;
            await db.SaveChangesAsync();
        });

        var sales = await client.GetAsync("/satis");
        Assert.Equal("/giris", sales.RequestMessage!.RequestUri!.AbsolutePath);
    }

    // ---- Yardımcılar ----

    private static Dictionary<string, string> Login(string email) => new() { ["Input.Email"] = email, ["Input.Password"] = MySqlWebAppFactory.StaffPassword };

    private async Task<string> CreateSellerAsync(string[] permissions) => (await factory.CreateStaffAsync(permissions)).Email;
}
