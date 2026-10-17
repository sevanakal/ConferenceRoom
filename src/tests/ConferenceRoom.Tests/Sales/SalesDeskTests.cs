using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Domain.Sales;
using ConferenceRoom.Tests.Auth;
using ConferenceRoom.Tests.Database;
using ConferenceRoom.Tests.Events;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Sales;

/// <summary>
/// Personel satış ekranı (gerçek MySQL): satış/rezervasyon kaydı, fiyatın kategoriden gelmesi, aynı koltuğun ikinci kez
/// satılamaması, alıcı adı ve ödeme kuralları, satışa kapalı koltuk ve seans, koltuk kapsamı (Pages/Satis/Seans, Web/Sales/SalesDesk.cs).
/// Örnek krokide koltuklar s1 (A-1), s2 (A-2), s3 (EK-1); s1 500 TL, s2 ücretsiz, s3 protokol.
/// </summary>
[Collection(MySqlCollection.Name)]
public partial class SalesDeskTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    private const string Map = """
        { "schema": 1,
          "categories": [
            { "id": "c1", "name": "1. Kategori", "price": 500, "color": "#1D5FBF", "protocol": false },
            { "id": "c0", "name": "Davetli", "price": 0, "color": "#0F766E", "protocol": false },
            { "id": "cp", "name": "Protokol", "price": 0, "color": "#6D3FC0", "protocol": true } ],
          "seats": { "s1": "c1", "s2": "c0", "s3": "cp" },
          "online": [] }
        """;

    [MySqlFact]
    public async Task Satis_kaydedilir_fiyat_kategoriden_gelir_ve_ayni_koltuk_ikinci_kez_satilamaz()
    {
        var sessionId = await CreateSessionAsync(SaleMode.Sale);
        var seller = await factory.SignInSellerAsync();

        var (status, answer) = await SellAsync(seller, sessionId, new { seats = new[] { "s1", "s2" }, buyerName = "  Ali Veli ", buyerPhone = "0532", note = "", paid = true });

        Assert.Equal("sold", status);
        Assert.Contains("2 bilet satıldı", answer.GetProperty("message").GetString());
        await factory.WithDbAsync(async db =>
        {
            var order = await db.Orders.Include(o => o.Tickets).SingleAsync(o => o.EventSessionId == sessionId);
            Assert.Equal(("Ali Veli", "0532", null as string), (order.BuyerName, order.BuyerPhone, order.Note));
            Assert.Equal((OrderKind.Sale, OrderChannel.Staff, PaymentStatus.Received, 500m), (order.Kind, order.Channel, order.Payment, order.Total));
            Assert.Equal(["A-1:500.00", "A-2:0.00"], order.Tickets.OrderBy(t => t.SeatId).Select(t => $"{t.SeatLabel}:{t.Price:0.00}"));
        });

        var (again, taken) = await SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ayşe", paid = false });
        Assert.Equal("taken", again);
        Assert.Contains("A-1", taken.GetProperty("message").GetString());
    }

    [MySqlFact]
    public async Task Alici_adi_zorunludur_odeme_toplam_sifirdan_buyukse_sorulur_sifirsa_sorulmaz()
    {
        var sessionId = await CreateSessionAsync(SaleMode.Reservation);
        var seller = await factory.SignInSellerAsync();

        Assert.Equal("invalid", (await SellAsync(seller, sessionId, new { seats = new[] { "s2" }, buyerName = "  " })).Status);
        var (noPayment, answer) = await SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ali" });
        Assert.Equal("invalid", noPayment);
        Assert.Contains("Ödemenin alınıp alınmadığını", answer.GetProperty("message").GetString());

        var (free, reserved) = await SellAsync(seller, sessionId, new { seats = new[] { "s2" }, buyerName = "Ali" });

        Assert.Equal("sold", free);
        Assert.Contains("rezerve edildi", reserved.GetProperty("message").GetString());
        await factory.WithDbAsync(async db =>
        {
            var order = await db.Orders.SingleAsync(o => o.EventSessionId == sessionId);
            Assert.Equal((OrderKind.Reservation, PaymentStatus.NotRequired, 0m), (order.Kind, order.Payment, order.Total));
        });
    }

    [MySqlFact]
    public async Task Protokol_koltugu_ve_satisi_acilmamis_seans_satilamaz()
    {
        var sessionId = await CreateSessionAsync(SaleMode.Sale);
        var seller = await factory.SignInSellerAsync();
        Assert.Equal("invalid", (await SellAsync(seller, sessionId, new { seats = new[] { "s3" }, buyerName = "Ali" })).Status);

        var notYet = await CreateSessionAsync(SaleMode.Sale, salesStartUtc: new DateTime(2029, 1, 1));
        Assert.Equal("closed", (await SellAsync(seller, notYet, new { seats = new[] { "s1" }, buyerName = "Ali", paid = true })).Status);
        await factory.WithDbAsync(async db => Assert.False(await db.Orders.AnyAsync(o => o.EventSessionId == sessionId || o.EventSessionId == notYet)));
    }

    [MySqlFact]
    public async Task Koltuk_kisitlamali_personel_yalnizca_kendisine_verilen_koltuklari_satar()
    {
        var sessionId = await CreateSessionAsync(SaleMode.Sale);
        var (email, staffId) = await factory.CreateStaffAsync([Permissions.SalesSell], seatsRestricted: true);
        await factory.WithDbAsync(async db =>
        {
            var hallId = await db.EventSessions.Where(s => s.Id == sessionId).Select(s => s.HallPlanVersion!.HallId).SingleAsync();
            db.SeatScopes.Add(new SeatScope { OrganizationId = Organization.DefaultId, HallId = hallId, ScopeJson = $$$"""{"schema":1,"staff":{"{{{staffId}}}":["s2"]}}""", UpdatedByStaffUserId = staffId });
            await db.SaveChangesAsync();
        });
        var seller = await factory.SignInSellerAsync(email, MySqlWebAppFactory.StaffPassword);

        Assert.Equal("invalid", (await SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ali", paid = true })).Status);
        Assert.Equal("sold", (await SellAsync(seller, sessionId, new { seats = new[] { "s2" }, buyerName = "Ali" })).Status);
    }

    [MySqlFact]
    public async Task Satis_listesi_satisa_acik_seansin_ekranina_baglanir_ekran_satis_oturumu_olmadan_acilmaz()
    {
        var sessionId = await CreateSessionAsync(SaleMode.Sale);
        var seller = await factory.SignInSellerAsync();

        Assert.Contains($"href=\"/satis/seans/{sessionId}\"", await seller.GetStringAsync("/satis"));
        Assert.Equal(HttpStatusCode.OK, (await seller.GetAsync($"/satis/seans/{sessionId}")).StatusCode);
        var anonymous = await factory.CreateClient().GetAsync($"/satis/seans/{sessionId}");
        Assert.Equal("/giris", anonymous.RequestMessage!.RequestUri!.AbsolutePath);
    }

    // ---- Yardımcılar ----

    /// <summary>Yayında, verilen satış türünde, koltuk ayarları yapılmış bir etkinlik ve seansı açar; seans no döner.</summary>
    private async Task<int> CreateSessionAsync(SaleMode mode, DateTime? salesStartUtc = null)
    {
        var (eventId, _) = await SeatScreens.CreateEventAsync(factory, withSession: true);
        var sessionId = 0;
        await factory.WithDbAsync(async db =>
        {
            var ev = await db.Events.SingleAsync(e => e.Id == eventId);
            ev.Status = EventStatus.Published;
            ev.SaleMode = mode;
            ev.SalesStartUtc = salesStartUtc;
            db.EventSeatMaps.Add(new EventSeatMap { OrganizationId = Organization.DefaultId, EventId = eventId, MapJson = Map, Revision = 1, UpdatedByStaffUserId = ev.CreatedByStaffUserId });
            await db.SaveChangesAsync();
            sessionId = await db.EventSessions.Where(s => s.EventId == eventId).Select(s => s.Id).SingleAsync();
        });
        return sessionId;
    }

    /// <summary>Ekranın yaptığı gibi satış isteğini gönderir; (durum, cevap) döner.</summary>
    private static async Task<(string Status, JsonElement Answer)> SellAsync(HttpClient client, int sessionId, object body)
    {
        var url = $"/satis/seans/{sessionId}";
        var page = await client.GetStringAsync(url);
        var config = JsonDocument.Parse(ConfigPattern().Match(page).Groups[1].Value).RootElement;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}?handler=Sell") { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", config.GetProperty("token").GetString());
        var response = await client.SendAsync(request);
        var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (answer.GetProperty("status").GetString()!, answer);
    }

    [GeneratedRegex("<script type=\"application/json\" id=\"editorConfig\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex ConfigPattern();
}
