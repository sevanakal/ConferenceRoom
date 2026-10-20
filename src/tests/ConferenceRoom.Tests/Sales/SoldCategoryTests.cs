using ConferenceRoom.Domain.Events;
using ConferenceRoom.Tests.Auth;
using ConferenceRoom.Tests.Database;
using ConferenceRoom.Tests.Events;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Sales;

/// <summary>
/// Bileti satılmış kategori (gerçek MySQL): koltuk ayarlarından silinemez, fiyatı değiştirilebilir ve eski bilet satış anındaki
/// fiyatında kalır (Web/Events/SeatMapStore.cs, Ticket.CategoryId).
/// </summary>
[Collection(MySqlCollection.Name)]
public class SoldCategoryTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    // Örnek ayarlarla (SalesTestData.Map) aynı; c1 kategorisi silinmiş.
    private const string WithoutC1 = """
        { "schema": 1,
          "categories": [
            { "id": "c0", "name": "Davetli", "price": 0, "color": "#0F766E", "protocol": false },
            { "id": "cp", "name": "Protokol", "price": 0, "color": "#6D3FC0", "protocol": true } ],
          "seats": { "s2": "c0", "s3": "cp" },
          "online": [] }
        """;

    [MySqlFact]
    public async Task Bileti_satilmis_kategori_silinemez_fiyati_degistirilebilir_eski_bilet_eski_fiyatta_kalir()
    {
        var (eventId, sessionId) = await CreateSoldSessionAsync();
        var admin = await factory.SignInStaffAsync();
        var page = $"/yonetim/fiyatlandirma/{eventId}";

        await factory.WithDbAsync(async db =>
            Assert.Equal("c1", await db.Tickets.Where(t => t.EventSessionId == sessionId).Select(t => t.CategoryId).SingleAsync()));
        Assert.Contains("\"soldCategories\":[\"c1\"]", await admin.GetStringAsync(page));

        Assert.Equal(("invalid", 1), await SeatScreens.SaveAsync(admin, page, WithoutC1, baseRevision: 1));

        Assert.Equal(("saved", 2), await SeatScreens.SaveAsync(admin, page, SalesTestData.Map.Replace("\"price\": 500", "\"price\": 750"), baseRevision: 1));
        await factory.WithDbAsync(async db =>
            Assert.Equal(500m, await db.Tickets.Where(t => t.EventSessionId == sessionId).Select(t => t.Price).SingleAsync()));
    }

    [MySqlFact]
    public async Task Kategori_kimligi_olmayan_eski_bilet_kategori_adiyla_eslesir_ve_kategori_silinemez()
    {
        var (eventId, sessionId) = await CreateSoldSessionAsync();
        await factory.WithDbAsync(db => db.Tickets.Where(t => t.EventSessionId == sessionId).ExecuteUpdateAsync(t => t.SetProperty(x => x.CategoryId, (string?)null)));
        var admin = await factory.SignInStaffAsync();

        Assert.Equal(("invalid", 1), await SeatScreens.SaveAsync(admin, $"/yonetim/fiyatlandirma/{eventId}", WithoutC1, baseRevision: 1));
    }

    [MySqlFact]
    public async Task Bileti_olmayan_kategori_silinebilir()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var eventId = await EventIdAsync(sessionId);
        var admin = await factory.SignInStaffAsync();

        Assert.Equal(("saved", 2), await SeatScreens.SaveAsync(admin, $"/yonetim/fiyatlandirma/{eventId}", WithoutC1, baseRevision: 1));
    }

    // ---- Yardımcılar ----

    /// <summary>Satışa açık seans açar ve s1 koltuğunu (c1, 500 TL) satar; (etkinlik no, seans no) döner.</summary>
    private async Task<(int EventId, int SessionId)> CreateSoldSessionAsync()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var seller = await factory.SignInSellerAsync();
        Assert.Equal("sold", (await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ali", paid = true })).Status);
        return (await EventIdAsync(sessionId), sessionId);
    }

    private async Task<int> EventIdAsync(int sessionId)
    {
        var id = 0;
        await factory.WithDbAsync(async db => id = await db.EventSessions.Where(s => s.Id == sessionId).Select(s => s.EventId).SingleAsync());
        return id;
    }
}
