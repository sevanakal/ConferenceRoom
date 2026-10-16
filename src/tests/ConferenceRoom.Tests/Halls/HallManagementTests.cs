using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Tests.Auth;
using ConferenceRoom.Tests.Database;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Halls;

/// <summary>
/// Salonlar ve kroki sürümleri (gerçek MySQL): salon ekleme, editörün kaydettiği krokinin sürüm sürüm saklanması,
/// iki kişinin aynı anda düzenlemesinde çakışmanın yakalanması, bozuk verinin reddi ve yetki kontrolü.
/// </summary>
[Collection(MySqlCollection.Name)]
public partial class HallManagementTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Yeni_salon_kaydedilince_kroki_editoru_bos_krokiyle_acilir()
    {
        var admin = await factory.SignInStaffAsync();
        var html = await Forms.PostAsync(admin, "/yonetim/salonlar/duzenle", [new("Input.Name", UniqueName()), new("Input.IsActive", "true")]);

        Assert.Contains("id=\"planData\"", html);
        Assert.Contains("\"version\":0", html);
    }

    [MySqlFact]
    public async Task Kroki_her_kaydedildiginde_yeni_surum_olur_degismediyse_surum_eklenmez()
    {
        var admin = await factory.SignInStaffAsync();
        var hallId = await CreateHallAsync(admin);

        var first = await SaveAsync(admin, hallId, baseVersion: 0, HallPlanTests.SamplePlan);
        Assert.Equal(("saved", 1), first);

        var same = await SaveAsync(admin, hallId, baseVersion: 1, HallPlanTests.SamplePlan);
        Assert.Equal(("unchanged", 1), same);

        var withoutExtraChair = HallPlanTests.SamplePlan.Replace("""{ "id": "s3", "levelId": "p", "type": "seat", "x": 220, "y": 200, "rotation": 0, "row": "EK", "num": 1, "kind": "extra", "blockId": null },""", "");
        var second = await SaveAsync(admin, hallId, baseVersion: 1, withoutExtraChair);
        Assert.Equal(("saved", 2), second);

        await factory.WithDbAsync(async db =>
        {
            var versions = await db.HallPlanVersions.Where(v => v.HallId == hallId).OrderBy(v => v.Version).ToListAsync();
            Assert.Equal([3, 2], versions.Select(v => v.Capacity));   // eski sürüm değişmeden durur
        });
    }

    [MySqlFact]
    public async Task Baskasi_yeni_surum_kaydettiyse_eski_surumu_temel_alan_kayit_reddedilir()
    {
        var admin = await factory.SignInStaffAsync();
        var hallId = await CreateHallAsync(admin);
        await SaveAsync(admin, hallId, baseVersion: 0, HallPlanTests.SamplePlan);

        // İkinci kişi editörü sürüm 1 yokken açmıştı (temel sürüm 0).
        var result = await SaveAsync(admin, hallId, baseVersion: 0, Domain.Halls.HallPlan.Blank("x", "Parter"));

        Assert.Equal(("conflict", 1), result);
    }

    [MySqlFact]
    public async Task Bozuk_kroki_verisi_kaydedilmez()
    {
        var admin = await factory.SignInStaffAsync();
        var hallId = await CreateHallAsync(admin);

        var result = await SaveAsync(admin, hallId, baseVersion: 0, """{ "schema": 1, "unit": "cm", "levels": [] }""");

        Assert.Equal(("invalid", 0), result);
        await factory.WithDbAsync(async db => Assert.False(await db.HallPlanVersions.AnyAsync(v => v.HallId == hallId)));
    }

    [MySqlFact]
    public async Task Salon_yetkisi_olmayan_kullanici_salonlara_giremez()
    {
        var admin = await factory.SignInStaffAsync();
        var email = $"satis-{Guid.NewGuid():N}@test.local";
        await Forms.PostAsync(admin, "/yonetim/kullanicilar/duzenle",
        [
            new("Input.FullName", "Satış Personeli"), new("Input.Email", email), new("Input.Password", "Satis.12345"),
            new("Input.RoleIds", Role.SalesManagerId.ToString()), new("Input.IsActive", "true"),
        ]);
        var sales = await factory.SignInStaffAsync(email, "Satis.12345");

        var response = await sales.GetAsync("/yonetim/salonlar");

        Assert.Contains("/yonetim/yetkisiz", response.RequestMessage!.RequestUri!.ToString());
    }

    // ---- Yardımcılar ----

    private static string UniqueName() => $"Salon {Guid.NewGuid():N}"[..20];

    private async Task<int> CreateHallAsync(HttpClient admin)
    {
        var name = UniqueName();
        await Forms.PostAsync(admin, "/yonetim/salonlar/duzenle", [new("Input.Name", name), new("Input.IsActive", "true")]);
        var id = 0;
        await factory.WithDbAsync(async db => id = await db.Halls.Where(h => h.Name == name).Select(h => h.Id).SingleAsync());
        return id;
    }

    /// <summary>Editörün Kaydet düğmesinin yaptığını yapar: sayfadaki anahtarla kroki JSON'unu gönderir.</summary>
    private static async Task<(string Status, int Version)> SaveAsync(HttpClient client, int hallId, int baseVersion, string planJson)
    {
        var page = await client.GetStringAsync($"/yonetim/salonlar/{hallId}/kroki");
        var config = JsonDocument.Parse(ConfigPattern().Match(page).Groups[1].Value).RootElement;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/yonetim/salonlar/{hallId}/kroki?handler=Save&baseVersion={baseVersion}")
        {
            Content = new StringContent(planJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("RequestVerificationToken", config.GetProperty("token").GetString());
        var response = await client.SendAsync(request);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);

        var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (answer.GetProperty("status").GetString()!, answer.GetProperty("version").GetInt32());
    }

    [GeneratedRegex("<script type=\"application/json\" id=\"editorConfig\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex ConfigPattern();
}
