using System.Net;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Tests.Auth;
using ConferenceRoom.Tests.Database;
using ConferenceRoom.Tests.Events;
using ConferenceRoom.Web.Sales;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Sales;

/// <summary>
/// Satış yetkileri (gerçek MySQL): personele salon ve etkinlik bazında koltuk verme, satışta kullanılacak kapsam sorusu,
/// çakışma ve yetki (Pages/Yonetim/SatisYetkileri, Web/Sales/SeatScopeStore.cs).
/// </summary>
[Collection(MySqlCollection.Name)]
public class SalesScopeTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Kisitli_personel_yalnizca_verilen_koltuklari_tam_yetkili_personel_hepsini_satar()
    {
        var admin = await factory.SignInStaffAsync();
        var (eventId, hallId) = await SeatScreens.CreateEventAsync(factory, withSession: true);
        var (_, restricted) = await SeatScreens.CreateStaffAsync(factory, admin, Role.SalesManagerId, seatsRestricted: true);
        var (_, empty) = await SeatScreens.CreateStaffAsync(factory, admin, Role.SalesManagerId, seatsRestricted: true);
        var (_, full) = await SeatScreens.CreateStaffAsync(factory, admin, Role.SalesManagerId);

        var (status, _) = await SaveAsync(admin, $"salon/{hallId}", $$"""{ "schema": 1, "staff": { "{{restricted}}": ["s1", "s2"] } }""", 0);

        Assert.Equal("saved", status);
        Assert.Equal(["s1", "s2"], (await AllowedAsync(restricted, eventId))!.Order());
        Assert.Empty((await AllowedAsync(empty, eventId))!);   // kısıtlı ama koltuk verilmemiş: hiç satamaz
        Assert.Null(await AllowedAsync(full, eventId));        // tam yetkili: tüm koltuklar
        Assert.True(SeatScopeStore.Allows(await AllowedAsync(restricted, eventId), ["s1"]));
        Assert.False(SeatScopeStore.Allows(await AllowedAsync(restricted, eventId), ["s1", "s3"]));
    }

    [MySqlFact]
    public async Task Etkinlige_ozel_kayit_salon_kaydinin_yerine_gecer_ve_silinince_salona_donulur()
    {
        var admin = await factory.SignInStaffAsync();
        var (eventId, hallId) = await SeatScreens.CreateEventAsync(factory, withSession: true);
        var (_, staff) = await SeatScreens.CreateStaffAsync(factory, admin, Role.SalesManagerId, seatsRestricted: true);
        await SaveAsync(admin, $"salon/{hallId}", $$"""{ "schema": 1, "staff": { "{{staff}}": ["s1"] } }""", 0);

        var page = await admin.GetStringAsync($"/yonetim/satis-yetkileri/etkinlik/{eventId}");
        Assert.Contains("salonun genel kaydını kullanıyor", page);
        await SaveAsync(admin, $"etkinlik/{eventId}", $$"""{ "schema": 1, "staff": { "{{staff}}": ["s3"] } }""", 0);
        Assert.Equal(["s3"], await AllowedAsync(staff, eventId));

        await Forms.PostAsync(admin, $"/yonetim/satis-yetkileri/etkinlik/{eventId}", [], $"/yonetim/satis-yetkileri/etkinlik/{eventId}?handler=Reset");

        Assert.Equal(["s1"], await AllowedAsync(staff, eventId));
    }

    [MySqlFact]
    public async Task Bu_arada_baskasi_kaydettiyse_kayit_reddedilir_ve_bilinmeyen_koltuk_kaydedilmez()
    {
        var admin = await factory.SignInStaffAsync();
        var (_, hallId) = await SeatScreens.CreateEventAsync(factory, withSession: true);
        var (_, staff) = await SeatScreens.CreateStaffAsync(factory, admin, Role.SalesManagerId, seatsRestricted: true);
        var scope = $$"""{ "schema": 1, "staff": { "{{staff}}": ["s1"] } }""";
        await SaveAsync(admin, $"salon/{hallId}", scope, 0);

        Assert.Equal(("conflict", 1), await SaveAsync(admin, $"salon/{hallId}", scope, 0));
        Assert.Equal("invalid", (await SaveAsync(admin, $"salon/{hallId}", scope.Replace("s1", "baska"), 1)).Status);
    }

    [MySqlFact]
    public async Task Kullanici_yonetimi_yetkisi_olmayan_satis_sorumlusu_satis_yetkilerine_giremez()
    {
        var admin = await factory.SignInStaffAsync();
        var (_, hallId) = await SeatScreens.CreateEventAsync(factory, withSession: true);
        var (sales, _) = await SeatScreens.CreateStaffAsync(factory, admin, Role.SalesManagerId);

        var denied = await sales.GetAsync($"/yonetim/satis-yetkileri/salon/{hallId}");
        var allowed = await admin.GetAsync($"/yonetim/satis-yetkileri/salon/{hallId}");

        Assert.Contains("/yonetim/yetkisiz", denied.RequestMessage!.RequestUri!.ToString());
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [MySqlFact]
    public async Task Koltuk_kisitlamasi_kullanici_duzenlemede_saklanir_ve_listede_gorunur()
    {
        var admin = await factory.SignInStaffAsync();
        var (_, staff) = await SeatScreens.CreateStaffAsync(factory, admin, Role.SalesManagerId, seatsRestricted: true);

        await factory.WithDbAsync(async db => Assert.True(await db.StaffUsers.Where(u => u.Id == staff).Select(u => u.SeatsRestricted).SingleAsync()));
        var page = await admin.GetStringAsync("/yonetim/satis-yetkileri");
        Assert.Contains("Yalnızca verilen koltuklar", page);
    }

    // ---- Yardımcılar ----

    private static Task<(string Status, int Revision)> SaveAsync(HttpClient client, string path, string json, int baseRevision) =>
        SeatScreens.SaveAsync(client, $"/yonetim/satis-yetkileri/{path}", json, baseRevision);

    private async Task<IReadOnlySet<string>?> AllowedAsync(int staffUserId, int eventId)
    {
        IReadOnlySet<string>? allowed = null;
        await factory.WithDbAsync(async db =>
        {
            var ev = await db.Events.SingleAsync(e => e.Id == eventId);
            allowed = await SeatScopeStore.AllowedSeatsAsync(db, staffUserId, ev, CancellationToken.None);
        });
        return allowed;
    }
}
