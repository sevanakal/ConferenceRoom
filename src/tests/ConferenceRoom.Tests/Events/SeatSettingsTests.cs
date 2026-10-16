using System.Net;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Tests.Auth;
using ConferenceRoom.Tests.Database;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Events;

/// <summary>
/// Etkinlik koltuk ayarları ekranı (gerçek MySQL): kayıt, çakışma, seanssız etkinlik ve yetki
/// (Pages/Yonetim/Fiyatlandirma, Web/Events/SeatMapStore.cs).
/// </summary>
[Collection(MySqlCollection.Name)]
public class SeatSettingsTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    private const string ValidMap = """
        { "schema": 1,
          "categories": [{ "id": "c1", "name": "1. Kategori", "price": 500, "color": "#1D5FBF", "protocol": false }],
          "seats": { "s1": "c1", "s2": "c1" },
          "online": ["s1"] }
        """;

    [MySqlFact]
    public async Task Koltuk_ayarlari_kaydedilir_ve_ozet_sayilar_saklanir()
    {
        var admin = await factory.SignInStaffAsync();
        var eventId = await CreateEventAsync(withSession: true);

        var (status, revision) = await SaveAsync(admin, eventId, ValidMap, baseRevision: 0);

        Assert.Equal(("saved", 1), (status, revision));
        await factory.WithDbAsync(async db =>
        {
            var map = await db.EventSeatMaps.SingleAsync(m => m.EventId == eventId);
            Assert.Equal((2, 1), (map.AssignedSeats, map.OnlineSeats));
        });
        var page = await admin.GetStringAsync("/yonetim/fiyatlandirma");
        Assert.Contains("2 koltuk kategorili", page);
    }

    [MySqlFact]
    public async Task Bu_arada_baskasi_kaydettiyse_kayit_reddedilir()
    {
        var admin = await factory.SignInStaffAsync();
        var eventId = await CreateEventAsync(withSession: true);
        await SaveAsync(admin, eventId, ValidMap, baseRevision: 0);

        var (status, revision) = await SaveAsync(admin, eventId, SeatMap.Empty, baseRevision: 0);

        Assert.Equal(("conflict", 1), (status, revision));
    }

    [MySqlFact]
    public async Task Krokide_olmayan_koltuk_kaydedilmez()
    {
        var admin = await factory.SignInStaffAsync();
        var eventId = await CreateEventAsync(withSession: true);

        var (status, _) = await SaveAsync(admin, eventId, ValidMap.Replace("\"s2\"", "\"baska-salon\""), baseRevision: 0);

        Assert.Equal("invalid", status);
        await factory.WithDbAsync(async db => Assert.False(await db.EventSeatMaps.AnyAsync(m => m.EventId == eventId)));
    }

    [MySqlFact]
    public async Task Seansi_olmayan_etkinlikte_once_seans_eklenmesi_istenir()
    {
        var admin = await factory.SignInStaffAsync();
        var eventId = await CreateEventAsync(withSession: false);

        var page = await admin.GetStringAsync($"/yonetim/fiyatlandirma/{eventId}");

        Assert.Contains("önce etkinliğe seans ekleyin", page);
    }

    [MySqlFact]
    public async Task Fiyatlandirma_yetkisi_olmayan_salon_sorumlusu_koltuk_ayarlarina_giremez_satis_sorumlusu_girer()
    {
        var admin = await factory.SignInStaffAsync();
        var eventId = await CreateEventAsync(withSession: true);
        var halls = await CreateStaffAsync(admin, Role.HallManagerId);
        var sales = await CreateStaffAsync(admin, Role.SalesManagerId);

        var denied = await halls.GetAsync($"/yonetim/fiyatlandirma/{eventId}");
        var allowed = await sales.GetAsync($"/yonetim/fiyatlandirma/{eventId}");

        Assert.Contains("/yonetim/yetkisiz", denied.RequestMessage!.RequestUri!.ToString());
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains("id=\"editorConfig\"", await allowed.Content.ReadAsStringAsync());
    }

    // ---- Yardımcılar ----

    private async Task<int> CreateEventAsync(bool withSession) => (await SeatScreens.CreateEventAsync(factory, withSession)).EventId;

    private async Task<HttpClient> CreateStaffAsync(HttpClient admin, int roleId) => (await SeatScreens.CreateStaffAsync(factory, admin, roleId)).Client;

    private static Task<(string Status, int Revision)> SaveAsync(HttpClient client, int eventId, string json, int baseRevision) =>
        SeatScreens.SaveAsync(client, $"/yonetim/fiyatlandirma/{eventId}", json, baseRevision);
}
