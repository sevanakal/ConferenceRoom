using System.Net;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Tests.Auth;
using ConferenceRoom.Tests.Database;
using ConferenceRoom.Web.Sales;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Sales;

/// <summary>
/// Bildirim rolü, satış bildirimleri ve satış raporu (gerçek MySQL): Web/Sales/SalesNotifier.cs, Web/Sales/SalesReport.cs,
/// Pages/Yonetim/Bildirimler, Pages/Yonetim/Satislar.
/// </summary>
[Collection(MySqlCollection.Name)]
public class SalesNotificationTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Bildirim_rolu_raporu_ve_bildirimleri_gorur_etkinlik_duzenleyemez_satis_yapamaz()
    {
        await factory.WithDbAsync(async db =>
            Assert.Equal([Permissions.SalesNotify, Permissions.SalesView],
                await db.RolePermissions.Where(p => p.RoleId == Role.NotifierId).Select(p => p.Permission).OrderBy(p => p).ToListAsync()));
        var (email, _) = await factory.CreateStaffInRoleAsync(Role.NotifierId);
        var notifier = await factory.SignInStaffAsync(email, MySqlWebAppFactory.StaffPassword);

        Assert.Equal(HttpStatusCode.OK, (await notifier.GetAsync("/yonetim/satislar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await notifier.GetAsync("/yonetim/bildirimler")).StatusCode);
        Assert.Contains("/yonetim/yetkisiz", (await notifier.GetAsync("/yonetim/etkinlikler")).RequestMessage!.RequestUri!.ToString());
        Assert.Contains("/yonetim/yetkisiz", (await notifier.GetAsync("/yonetim/fiyatlandirma")).RequestMessage!.RequestUri!.ToString());

        var seller = await factory.SignInSellerAsync(email, MySqlWebAppFactory.StaffPassword);
        Assert.Equal("/giris", (await seller.GetAsync("/satis")).RequestMessage!.RequestUri!.AbsolutePath);
    }

    [MySqlFact]
    public async Task Satis_olunca_yalnizca_bildirim_yetkisi_olan_aktif_kullanicilar_bildirim_alir_liste_acilinca_okundu_sayilir()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var (email, notifierId) = await factory.CreateStaffInRoleAsync(Role.NotifierId);
        var (_, passiveId) = await factory.CreateStaffInRoleAsync(Role.NotifierId, isActive: false);
        var (_, plainId) = await factory.CreateStaffAsync([Permissions.SalesView]);
        var seller = await factory.SignInSellerAsync();

        Assert.Equal("sold", (await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1", "s2" }, buyerName = "Ali Veli", paid = false })).Status);

        var orderId = 0;
        await factory.WithDbAsync(async db =>
        {
            orderId = await db.Orders.Where(o => o.EventSessionId == sessionId).Select(o => o.Id).SingleAsync();
            var recipients = await db.Notifications.Where(n => n.OrderId == orderId).Select(n => n.StaffUserId).ToListAsync();
            Assert.Contains(notifierId, recipients);
            Assert.DoesNotContain(passiveId, recipients);
            Assert.DoesNotContain(plainId, recipients);
        });

        var notifier = await factory.SignInStaffAsync(email, MySqlWebAppFactory.StaffPassword);
        Assert.Contains("id=\"notifyCount\">1</span>", await notifier.GetStringAsync("/yonetim"));
        var list = await notifier.GetStringAsync("/yonetim/bildirimler");
        Assert.Contains("2 bilet satıldı", list);
        Assert.Contains($"/yonetim/satislar/seans/{sessionId}#islem-{orderId}", list);
        Assert.Contains("id=\"notifyCount\" hidden=\"hidden\">", await notifier.GetStringAsync("/yonetim"));
    }

    [MySqlFact]
    public async Task Satis_raporu_satilan_rezerve_kalan_koltugu_ve_bekleyen_odemeyi_gosterir()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var seller = await factory.SignInSellerAsync();
        await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ali Veli", buyerPhone = "0532 000 00 00", paid = false });

        await factory.WithDbAsync(async db =>
        {
            var row = (await SalesReport.SessionsAsync(db, includePast: true, DateTime.UtcNow, CancellationToken.None, sessionId)).Single();
            Assert.Equal((2, 1, 0, 1, 0m, 500m), (row.Capacity, row.Sold, row.Reserved, row.Remaining, row.Received, row.Pending));
        });

        var (email, _) = await factory.CreateStaffInRoleAsync(Role.NotifierId);
        var notifier = await factory.SignInStaffAsync(email, MySqlWebAppFactory.StaffPassword);
        Assert.Contains($"/yonetim/satislar/seans/{sessionId}", await notifier.GetStringAsync("/yonetim/satislar"));
        var detail = await notifier.GetStringAsync($"/yonetim/satislar/seans/{sessionId}");
        Assert.Contains("Ali Veli", detail);
        Assert.Contains("0532 000 00 00", detail);
        Assert.Contains(">A-1<", detail);
        Assert.Contains("Alınmadı", detail);
    }
}
