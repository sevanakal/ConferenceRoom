using System.Net;
using System.Text.RegularExpressions;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Tests.Database;
using ConferenceRoom.Web.Auth;

namespace ConferenceRoom.Tests.Auth;

/// <summary>
/// Gerçek MySQL üzerinde uçtan uca giriş: yönetim girişi, rol yetkileri, izleyici kaydı ve iki tarafın ayrılığı.
/// Formlar tarayıcı gibi gönderilir (önce sayfa açılır, sahtecilik önleme anahtarı formdan alınır).
/// </summary>
[Collection(MySqlCollection.Name)]
public class LoginFlowTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Yanlis_sifreyle_yonetim_girisi_yapilamaz()
    {
        var client = factory.CreateClient();
        var html = await Forms.PostAsync(client, "/yonetim/giris", new Dictionary<string, string> { ["Input.Email"] = MySqlWebAppFactory.AdminEmail, ["Input.Password"] = "yanlis" });
        Assert.Contains("E-posta ya da şifre hatalı.", html);
        Assert.Equal(HttpStatusCode.Redirect, (await NoRedirect().GetAsync("/yonetim")).StatusCode);
    }

    [MySqlFact]
    public async Task Ilk_yonetici_giris_yapinca_panelde_tum_bolumler_acik_gorunur()
    {
        var client = factory.CreateClient();
        // E-posta büyük harf ve boşlukla yazılsa da bulunur.
        var html = await Forms.PostAsync(client, "/yonetim/giris", new Dictionary<string, string> { ["Input.Email"] = " YONETICI@test.local", ["Input.Password"] = MySqlWebAppFactory.AdminPassword });
        Assert.Contains("Hoş geldiniz, Test Yöneticisi", html);
        Assert.Equal(Permissions.All.Count + 1, Regex.Count(html, "data-allowed=\"true\""));   // yetkiler + satış yetkileri kartı
        Assert.DoesNotContain("data-allowed=\"false\"", html);
    }

    [MySqlFact]
    public async Task Satis_sorumlusu_yalnizca_kendi_yetkilerini_acik_gorur()
    {
        var email = $"satis-{Guid.NewGuid():N}@test.local";
        await factory.WithDbAsync(async db =>
        {
            db.StaffUsers.Add(new StaffUser
            {
                OrganizationId = Organization.DefaultId,
                Email = email,
                FullName = "Satış Sorumlusu",
                PasswordHash = new PasswordService().Hash("Satis.Sifre.1"),
                Roles = [new StaffUserRole { RoleId = Role.SalesManagerId }],
            });
            await db.SaveChangesAsync();
        });

        var client = factory.CreateClient();
        var html = await Forms.PostAsync(client, "/yonetim/giris", new Dictionary<string, string> { ["Input.Email"] = email, ["Input.Password"] = "Satis.Sifre.1" });
        Assert.Equal(3, Regex.Count(html, "data-allowed=\"true\""));      // fiyatlar, satış yapma, satışları görme
        Assert.Equal(4, Regex.Count(html, "data-allowed=\"false\""));     // salonlar, etkinlikler, kullanıcılar, satış yetkileri kilitli
    }

    [MySqlFact]
    public async Task Izleyici_kayit_olur_ama_yonetim_paneline_ve_yonetim_girisine_giremez()
    {
        var email = $"izleyici-{Guid.NewGuid():N}@test.local";
        var client = factory.CreateClient();
        var home = await Forms.PostAsync(client, "/kayit", new Dictionary<string, string>
        {
            ["Input.FullName"] = "Ayşe Yılmaz",
            ["Input.Email"] = email,
            ["Input.Password"] = "izleyici-sifre",
            ["Input.Consent"] = "true",
        });
        Assert.Contains("Ayşe Yılmaz", home);   // kayıttan sonra giriş yapmış olarak ana sayfaya döner

        // İzleyici çerezi yönetimde geçmez: panel yönetim girişine gönderir.
        var panel = await client.GetAsync("/yonetim");
        Assert.Contains("/yonetim/giris", panel.RequestMessage!.RequestUri!.ToString());

        // Aynı bilgilerle yönetim girişi de yapılamaz (iki taraf ayrı tablolardadır).
        var html = await Forms.PostAsync(client, "/yonetim/giris", new Dictionary<string, string> { ["Input.Email"] = email, ["Input.Password"] = "izleyici-sifre" });
        Assert.Contains("E-posta ya da şifre hatalı.", html);
    }

    [MySqlFact]
    public async Task Yonetici_bilgileriyle_genel_giristen_panel_acilmaz()
    {
        var client = factory.CreateClient();

        // Genel giriş personeli yalnızca satış bölümüne alır (yöneticinin satış yetkisi de var); panel ayrı çerezle,
        // yalnızca yönetim girişinden açılır. Adresteki dönüş adresi de satış bölümü dışına çıkaramaz.
        var html = await Forms.PostAsync(client, "/giris?returnUrl=%2Fyonetim", new Dictionary<string, string>
        {
            ["Input.Email"] = MySqlWebAppFactory.AdminEmail,
            ["Input.Password"] = MySqlWebAppFactory.AdminPassword,
        });
        Assert.DoesNotContain("admin-subnav", html);

        var panel = await client.GetAsync("/yonetim");
        Assert.Contains("/yonetim/giris", panel.RequestMessage!.RequestUri!.ToString());
    }

    [MySqlFact]
    public async Task Ayni_e_postayla_ikinci_kez_kayit_olunamaz()
    {
        var form = new Dictionary<string, string>
        {
            ["Input.FullName"] = "Ali Veli",
            ["Input.Email"] = $"cift-{Guid.NewGuid():N}@test.local",
            ["Input.Password"] = "12345678",
            ["Input.Consent"] = "true",
        };
        await Forms.PostAsync(factory.CreateClient(), "/kayit", form);
        var html = await Forms.PostAsync(factory.CreateClient(), "/kayit", form);
        Assert.Contains("Bu e-posta adresiyle zaten bir hesap var.", html);
    }

    // ---- Yardımcılar ----

    private HttpClient NoRedirect() => factory.CreateClient(new() { AllowAutoRedirect = false });
}
