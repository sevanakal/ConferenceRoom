using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Tests.Auth;
using ConferenceRoom.Tests.Database;
using ConferenceRoom.Tests.Events;

namespace ConferenceRoom.Tests.Admin;

/// <summary>
/// Panel kart sırası (gerçek MySQL): kullanıcı kartları sürükleyip bırakınca sayfanın yaptığı gibi sıra gönderilir
/// (Pages/Yonetim/Index.cshtml.cs, OnPostOrderAsync); sıra kullanıcı başınadır.
/// </summary>
[Collection(MySqlCollection.Name)]
public partial class DashboardOrderTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Kart_sirasi_kullaniciya_ozel_saklanir_ve_baska_kullanicinin_paneli_degismez()
    {
        var admin = await factory.SignInStaffAsync();
        var (mine, _) = await SeatScreens.CreateStaffAsync(factory, admin, Role.SalesManagerId);
        var (other, _) = await SeatScreens.CreateStaffAsync(factory, admin, Role.SalesManagerId);

        var response = await SaveAsync(mine, """["salesScope", "users.manage", "olmayan.kart", "salesScope"]""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var expected = new[] { "salesScope", "users.manage" }.Concat(DefaultOrder.Except(["salesScope", "users.manage"]));
        Assert.Equal(expected, await OrderAsync(mine));   // bilinmeyen ve tekrarlanan anahtar atılır, kalanlar sona
        Assert.Equal(DefaultOrder, await OrderAsync(other));
    }

    [MySqlFact]
    public async Task Bos_sira_gonderilince_varsayilan_siraya_donulur()
    {
        var admin = await factory.SignInStaffAsync();
        var (staff, _) = await SeatScreens.CreateStaffAsync(factory, admin, Role.SalesManagerId);
        await SaveAsync(staff, """["salesScope"]""");

        await SaveAsync(staff, "[]");

        Assert.Equal(DefaultOrder, await OrderAsync(staff));
    }

    [MySqlFact]
    public async Task Sahtecilik_anahtari_olmadan_kart_sirasi_kaydedilmez()
    {
        var admin = await factory.SignInStaffAsync();
        var (staff, _) = await SeatScreens.CreateStaffAsync(factory, admin, Role.SalesManagerId);

        var response = await staff.PostAsync("/yonetim?handler=Order", new StringContent("""["salesScope"]""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DefaultOrder, await OrderAsync(staff));
    }

    // ---- Yardımcılar ----

    private static readonly string[] DefaultOrder = [.. ConferenceRoom.Web.Pages.Yonetim.IndexModel.CardKeys];

    /// <summary>Panelin yaptığı gibi sırayı gönderir (anahtar sayfadaki data-token alanından).</summary>
    private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, string json)
    {
        var page = await client.GetStringAsync("/yonetim");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/yonetim?handler=Order") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("RequestVerificationToken", TokenPattern().Match(page).Groups[1].Value);
        return await client.SendAsync(request);
    }

    private static async Task<string[]> OrderAsync(HttpClient client) =>
        [.. KeyPattern().Matches(await client.GetStringAsync("/yonetim")).Select(m => m.Groups[1].Value)];

    [GeneratedRegex("data-token=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("<li class=\"section-card\"[^>]*data-key=\"([^\"]+)\"")]
    private static partial Regex KeyPattern();
}
