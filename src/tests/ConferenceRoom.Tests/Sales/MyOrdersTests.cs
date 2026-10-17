using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Sales;
using ConferenceRoom.Tests.Auth;
using ConferenceRoom.Tests.Database;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Sales;

/// <summary>
/// Satışlarım (gerçek MySQL): satış personeli kendi işlemlerini görür, ödemeyi sonradan günceller; başkasının işlemini ve
/// ücretsiz işlemi değiştiremez (Pages/Satis/Satislarim, SalesDesk.SetPaymentAsync). "Satış kayıtlarını düzeltme" yetkisi
/// olan yönetim kullanıcısı her işlemin ödemesini satış raporundan değiştirir (Pages/Yonetim/Satislar/Seans).
/// </summary>
[Collection(MySqlCollection.Name)]
public class MyOrdersTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Personel_kendi_satislarini_gorur_ve_odemeyi_sonradan_alindi_yapar()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var seller = await factory.SignInSellerAsync();
        await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ali Veli", buyerPhone = "0532 000 00 00", paid = false });
        var orderId = await OrderIdAsync(sessionId, "s1");

        var page = await seller.GetStringAsync("/satis/satislarim");
        Assert.Contains("Ali Veli", page);
        Assert.Contains("0532 000 00 00", page);
        Assert.Contains($"id=\"islem-{orderId}\"", page);
        Assert.Contains("Alınmadı", page);

        var after = await SetPaymentAsync(seller, orderId, paid: true);
        Assert.Contains("ödemesi alındı olarak işaretlendi", after);
        Assert.Equal(PaymentStatus.Received, await PaymentAsync(orderId));

        await SetPaymentAsync(seller, orderId, paid: false);
        Assert.Equal(PaymentStatus.Pending, await PaymentAsync(orderId));
    }

    [MySqlFact]
    public async Task Baska_personelin_islemi_ve_ucretsiz_islemin_odemesi_degistirilemez()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var admin = await factory.SignInSellerAsync();
        await SalesTestData.SellAsync(admin, sessionId, new { seats = new[] { "s1" }, buyerName = "Ali", paid = false });
        await SalesTestData.SellAsync(admin, sessionId, new { seats = new[] { "s2" }, buyerName = "Ayşe" });
        var paidOrder = await OrderIdAsync(sessionId, "s1");
        var freeOrder = await OrderIdAsync(sessionId, "s2");

        // Ücretsiz işlem: ödeme sorulmadığı için değişmez.
        await SetPaymentAsync(admin, freeOrder, paid: true);
        Assert.Equal(PaymentStatus.NotRequired, await PaymentAsync(freeOrder));

        // Başka personel: kendi listesinde görmez, ödemesini değiştiremez.
        var (email, _) = await factory.CreateStaffAsync([Permissions.SalesSell]);
        var other = await factory.SignInSellerAsync(email, MySqlWebAppFactory.StaffPassword);
        var otherSession = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        await SalesTestData.SellAsync(other, otherSession, new { seats = new[] { "s1" }, buyerName = "Can", paid = false });
        Assert.DoesNotContain($"islem-{paidOrder}\"", await other.GetStringAsync("/satis/satislarim"));
        await SetPaymentAsync(other, paidOrder, paid: true);
        Assert.Equal(PaymentStatus.Pending, await PaymentAsync(paidOrder));
    }

    [MySqlFact]
    public async Task Duzeltme_yetkisi_olan_yonetici_baskasinin_islem_odemesini_rapordan_degistirir_yalnizca_rapor_goren_degistiremez()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var (sellerEmail, _) = await factory.CreateStaffAsync([Permissions.SalesSell]);
        var seller = await factory.SignInSellerAsync(sellerEmail, MySqlWebAppFactory.StaffPassword);
        await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ali", paid = false });
        await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s2" }, buyerName = "Ayşe" });
        var paidOrder = await OrderIdAsync(sessionId, "s1");
        var freeOrder = await OrderIdAsync(sessionId, "s2");
        var report = $"/yonetim/satislar/seans/{sessionId}";

        // Yalnızca rapor gören (Bildirim rolü): düğme yok, istek gönderse de değişmez.
        var (notifierEmail, _) = await factory.CreateStaffInRoleAsync(Role.NotifierId);
        var notifier = await factory.SignInStaffAsync(notifierEmail, MySqlWebAppFactory.StaffPassword);
        Assert.DoesNotContain("handler=Payment", await notifier.GetStringAsync(report));
        await SetReportPaymentAsync(notifier, report, paidOrder, paid: true);
        Assert.Equal(PaymentStatus.Pending, await PaymentAsync(paidOrder));

        // Yönetici (tüm yetkiler, sales.manage dahil): başkasının işlemini alındı yapar, geri alır; ücretsiz işlem değişmez.
        var admin = await factory.SignInStaffAsync();
        Assert.Contains("handler=Payment", await admin.GetStringAsync(report));
        await SetReportPaymentAsync(admin, report, paidOrder, paid: true);
        Assert.Equal(PaymentStatus.Received, await PaymentAsync(paidOrder));
        await SetReportPaymentAsync(admin, report, paidOrder, paid: false);
        Assert.Equal(PaymentStatus.Pending, await PaymentAsync(paidOrder));
        await SetReportPaymentAsync(admin, report, freeOrder, paid: true);
        Assert.Equal(PaymentStatus.NotRequired, await PaymentAsync(freeOrder));
    }

    // ---- Yardımcılar ----

    private static Task<string> SetReportPaymentAsync(HttpClient client, string report, int orderId, bool paid) =>
        Forms.PostAsync(client, report, [new("orderId", orderId.ToString()), new("paid", paid ? "true" : "false")], $"{report}?handler=Payment");

    private static Task<string> SetPaymentAsync(HttpClient client, int orderId, bool paid) =>
        Forms.PostAsync(client, "/satis/satislarim", [new("orderId", orderId.ToString()), new("paid", paid ? "true" : "false")],
            "/satis/satislarim?handler=Payment");

    private async Task<int> OrderIdAsync(int sessionId, string seatId)
    {
        var id = 0;
        await factory.WithDbAsync(async db => id = await db.Tickets.Where(t => t.EventSessionId == sessionId && t.SeatId == seatId).Select(t => t.OrderId).SingleAsync());
        return id;
    }

    private async Task<PaymentStatus> PaymentAsync(int orderId)
    {
        var payment = PaymentStatus.NotRequired;
        await factory.WithDbAsync(async db => payment = await db.Orders.Where(o => o.Id == orderId).Select(o => o.Payment).SingleAsync());
        return payment;
    }
}
