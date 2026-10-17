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
using ConferenceRoom.Web.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConferenceRoom.Tests.Sales;

/// <summary>
/// Personel satış ekranı (gerçek MySQL): satış/rezervasyon kaydı, fiyatın kategoriden gelmesi, aynı koltuğun ikinci kez
/// satılamaması, başka ekranda ayrılmış koltuk, alıcı adı ve ödeme kuralları, satışa kapalı koltuk ve seans, koltuk kapsamı
/// (Pages/Satis/Seans, Web/Sales/SalesDesk.cs).
/// Örnek koltuk ayarları ve yardımcılar SalesTestData.cs içinde.
/// </summary>
[Collection(MySqlCollection.Name)]
public class SalesDeskTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Satis_kaydedilir_fiyat_kategoriden_gelir_ve_ayni_koltuk_ikinci_kez_satilamaz()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var seller = await factory.SignInSellerAsync();

        var (status, answer) = await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1", "s2" }, buyerName = "  Ali Veli ", buyerPhone = "0532", note = "", paid = true });

        Assert.Equal("sold", status);
        Assert.Contains("2 bilet satıldı", answer.GetProperty("message").GetString());
        await factory.WithDbAsync(async db =>
        {
            var order = await db.Orders.Include(o => o.Tickets).SingleAsync(o => o.EventSessionId == sessionId);
            Assert.Equal(("Ali Veli", "0532", null as string), (order.BuyerName, order.BuyerPhone, order.Note));
            Assert.Equal((OrderKind.Sale, OrderChannel.Staff, PaymentStatus.Received, 500m), (order.Kind, order.Channel, order.Payment, order.Total));
            Assert.Equal(["A-1:500.00", "A-2:0.00"], order.Tickets.OrderBy(t => t.SeatId).Select(t => $"{t.SeatLabel}:{t.Price:0.00}"));
        });

        var (again, taken) = await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ayşe", paid = false });
        Assert.Equal("taken", again);
        Assert.Contains("A-1", taken.GetProperty("message").GetString());
    }

    [MySqlFact]
    public async Task Alici_adi_zorunludur_odeme_toplam_sifirdan_buyukse_sorulur_sifirsa_sorulmaz()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Reservation);
        var seller = await factory.SignInSellerAsync();

        Assert.Equal("invalid", (await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s2" }, buyerName = "  " })).Status);
        var (noPayment, answer) = await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ali" });
        Assert.Equal("invalid", noPayment);
        Assert.Contains("Ödemenin alınıp alınmadığını", answer.GetProperty("message").GetString());

        var (free, reserved) = await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s2" }, buyerName = "Ali" });

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
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var seller = await factory.SignInSellerAsync();
        Assert.Equal("invalid", (await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s3" }, buyerName = "Ali" })).Status);

        var notYet = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale, salesStartUtc: new DateTime(2029, 1, 1));
        Assert.Equal("closed", (await SalesTestData.SellAsync(seller, notYet, new { seats = new[] { "s1" }, buyerName = "Ali", paid = true })).Status);
        await factory.WithDbAsync(async db => Assert.False(await db.Orders.AnyAsync(o => o.EventSessionId == sessionId || o.EventSessionId == notYet)));
    }

    [MySqlFact]
    public async Task Koltuk_kisitlamali_personel_yalnizca_kendisine_verilen_koltuklari_satar()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var (email, staffId) = await factory.CreateStaffAsync([Permissions.SalesSell], seatsRestricted: true);
        await factory.WithDbAsync(async db =>
        {
            var hallId = await db.EventSessions.Where(s => s.Id == sessionId).Select(s => s.HallPlanVersion!.HallId).SingleAsync();
            db.SeatScopes.Add(new SeatScope { OrganizationId = Organization.DefaultId, HallId = hallId, ScopeJson = $$$"""{"schema":1,"staff":{"{{{staffId}}}":["s2"]}}""", UpdatedByStaffUserId = staffId });
            await db.SaveChangesAsync();
        });
        var seller = await factory.SignInSellerAsync(email, MySqlWebAppFactory.StaffPassword);

        Assert.Equal("invalid", (await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ali", paid = true })).Status);
        Assert.Equal("sold", (await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s2" }, buyerName = "Ali" })).Status);
    }

    [MySqlFact]
    public async Task Baska_ekranda_ayrilmis_koltuk_satilamaz_ayrim_suresi_dolunca_satilir()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var seller = await factory.SignInSellerAsync();
        var holds = factory.Services.GetRequiredService<SeatHolds>();
        holds.TryHold(sessionId, "baska-ekran", 999, ["s1"]);

        var (status, answer) = await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ali", paid = true, holder = "baska-ekran" });

        Assert.Equal("held", status);
        Assert.Contains("A-1", answer.GetProperty("message").GetString());
        holds.Release(sessionId, "baska-ekran", ["s1"]);
        Assert.Equal("sold", (await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ali", paid = true })).Status);
    }

    [MySqlFact]
    public async Task Satis_listesi_satisa_acik_seansin_ekranina_baglanir_ekran_satis_oturumu_olmadan_acilmaz()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var seller = await factory.SignInSellerAsync();

        Assert.Contains($"href=\"/satis/seans/{sessionId}\"", await seller.GetStringAsync("/satis"));
        Assert.Equal(HttpStatusCode.OK, (await seller.GetAsync($"/satis/seans/{sessionId}")).StatusCode);
        var anonymous = await factory.CreateClient().GetAsync($"/satis/seans/{sessionId}");
        Assert.Equal("/giris", anonymous.RequestMessage!.RequestUri!.AbsolutePath);
    }
}
