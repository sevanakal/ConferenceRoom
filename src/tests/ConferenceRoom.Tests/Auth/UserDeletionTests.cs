using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Tests.Database;
using ConferenceRoom.Tests.Sales;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Auth;

/// <summary>
/// Kullanıcı silme (gerçek MySQL; Pages/Yonetim/Kullanicilar, StaffAccounts.DeleteAsync): silinen kullanıcı listeden kalkar,
/// açık oturumu kapanır, e-postası yeniden kullanılabilir; yaptığı satışlarda adı görünmeye devam eder.
/// Yönetici rolündeki kullanıcı ve kişinin kendisi silinemez.
/// </summary>
[Collection(MySqlCollection.Name)]
public class UserDeletionTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Silinen_personel_listeden_kalkar_giremez_ve_eski_satisinda_adi_gorunur()
    {
        // Personel bir satış yapar.
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var (email, id) = await factory.CreateStaffAsync([Permissions.SalesSell]);
        var name = $"Silinecek {Guid.NewGuid():N}"[..24];
        await factory.WithDbAsync(async db =>
        {
            (await db.StaffUsers.SingleAsync(u => u.Id == id)).FullName = name;
            await db.SaveChangesAsync();
        });
        var seller = await factory.SignInSellerAsync(email, MySqlWebAppFactory.StaffPassword);
        await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1" }, buyerName = "Ali", paid = true });

        // Yönetici listeden siler.
        var admin = await factory.SignInStaffAsync();
        Assert.Contains($"/yonetim/kullanicilar/duzenle/{id}?handler=Delete", await admin.GetStringAsync("/yonetim/kullanicilar"));
        var html = await Forms.PostAsync(admin, $"/yonetim/kullanicilar/duzenle/{id}", [], postUrl: $"/yonetim/kullanicilar/duzenle/{id}?handler=Delete");
        Assert.Contains("Kullanıcı silindi.", html);
        Assert.DoesNotContain(name, html);

        // Açık oturumu kapanır, düzenleme sayfası yoktur; satış raporunda satan olarak adı görünür.
        Assert.Contains("/giris", (await seller.GetAsync("/satis")).RequestMessage!.RequestUri!.ToString());
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await admin.GetAsync($"/yonetim/kullanicilar/duzenle/{id}")).StatusCode);
        Assert.Contains(name, await admin.GetStringAsync($"/yonetim/satislar/seans/{sessionId}"));
        await factory.WithDbAsync(async db =>
        {
            var user = await db.StaffUsers.Include(u => u.Roles).SingleAsync(u => u.Id == id);
            Assert.NotNull(user.DeletedAtUtc);
            Assert.False(user.IsActive);
            Assert.Empty(user.Roles);
        });

        // E-postası yeni bir kullanıcıya verilebilir.
        var created = await Forms.PostAsync(admin, "/yonetim/kullanicilar/duzenle",
        [
            new("Input.FullName", "Yeni personel"), new("Input.Email", email), new("Input.Password", "Personel.123"),
            new("Input.RoleIds", Role.SalesManagerId.ToString()), new("Input.IsActive", "true"),
        ]);
        Assert.Contains("Kullanıcı kaydedildi.", created);
    }

    [MySqlFact]
    public async Task Yonetici_rolundeki_kullanici_ve_kisinin_kendisi_silinemez()
    {
        var admin = await factory.SignInStaffAsync();
        var (_, otherAdminId) = await factory.CreateStaffInRoleAsync(Role.AdministratorId);
        var url = $"/yonetim/kullanicilar/duzenle/{otherAdminId}";
        Assert.DoesNotContain($"{url}?handler=Delete", await admin.GetStringAsync("/yonetim/kullanicilar"));
        Assert.Contains("Yönetici rolündeki kullanıcı silinemez.", await Forms.PostAsync(admin, url, [], postUrl: url + "?handler=Delete"));

        int selfId = 0;
        await factory.WithDbAsync(async db => selfId = await db.StaffUsers.Where(u => u.Email == MySqlWebAppFactory.AdminEmail).Select(u => u.Id).SingleAsync());
        var selfUrl = $"/yonetim/kullanicilar/duzenle/{selfId}";
        Assert.Contains("Kendi hesabınızı silemezsiniz.", await Forms.PostAsync(admin, selfUrl, [], postUrl: selfUrl + "?handler=Delete"));

        await factory.WithDbAsync(async db =>
            Assert.False(await db.StaffUsers.AnyAsync(u => (u.Id == otherAdminId || u.Id == selfId) && u.DeletedAtUtc != null)));
    }
}
