using System.Net;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Tests.Database;
using ConferenceRoom.Web.Auth;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Auth;

/// <summary>
/// Kullanıcı ve rol yönetimi (gerçek MySQL): rol/kullanıcı kaydı, panelin kilitlenmesini önleyen kurallar,
/// yetki değişikliğinin ve pasif yapmanın açık oturuma hemen yansıması.
/// </summary>
[Collection(MySqlCollection.Name)]
public class UserRoleManagementTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Yeni_rol_secilen_yetkilerle_kaydedilir()
    {
        var admin = await SignInAdminAsync();
        var name = $"Gişe {Guid.NewGuid():N}"[..20];
        var html = await Forms.PostAsync(admin, "/yonetim/roller/duzenle",
        [
            new("Input.Name", name), new("Input.Description", "Kapıda satış"),
            new("Input.Permissions", Permissions.SalesView), new("Input.Permissions", "olmayan.yetki"),
        ]);
        Assert.Contains("Rol kaydedildi.", html);

        await factory.WithDbAsync(async db =>
        {
            var role = await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Name == name);
            Assert.Equal([Permissions.SalesView], role.Permissions.Select(p => p.Permission));   // bilinmeyen kod kaydedilmez
        });
    }

    [MySqlFact]
    public async Task Son_yoneticinin_kullanici_yonetimi_yetkisi_kaldirilamaz()
    {
        var admin = await SignInAdminAsync();
        // Yönetici rolünden "Kullanıcılar ve roller" yetkisini kaldırmayı dene (testteki tek yönetici bu rolde).
        var html = await Forms.PostAsync(admin, $"/yonetim/roller/duzenle/{Role.AdministratorId}",
        [
            new("Input.Name", "Yönetici"), new("Input.Permissions", Permissions.HallsManage),
        ]);
        Assert.Contains("kullanıcıları ve rolleri yönetebilecek aktif kimse kalmazdı", html);

        await factory.WithDbAsync(async db =>
            Assert.True(await db.RolePermissions.AnyAsync(p => p.RoleId == Role.AdministratorId && p.Permission == Permissions.UsersManage)));
    }

    [MySqlFact]
    public async Task Sistem_rolu_silinemez()
    {
        var admin = await SignInAdminAsync();
        var url = $"/yonetim/roller/duzenle/{Role.AdministratorId}";
        var html = await Forms.PostAsync(admin, url, [], postUrl: url + "?handler=Delete");
        Assert.Contains("Kurulumla gelen sistem rolü silinemez.", html);
    }

    [MySqlFact]
    public async Task Kullanici_kendi_hesabini_pasif_yapamaz()
    {
        var admin = await SignInAdminAsync();
        var adminId = await IdOfAsync(MySqlWebAppFactory.AdminEmail);
        var html = await Forms.PostAsync(admin, $"/yonetim/kullanicilar/duzenle/{adminId}",
        [
            new("Input.FullName", "Test Yöneticisi"), new("Input.Email", MySqlWebAppFactory.AdminEmail),
            new("Input.RoleIds", Role.AdministratorId.ToString()), new("Input.IsActive", "false"),
        ]);
        Assert.Contains("Kendi hesabınızı pasif yapamazsınız.", html);
    }

    [MySqlFact]
    public async Task Yetki_degisikligi_acik_oturumda_yeniden_giris_gerekmeden_gecerli_olur()
    {
        var admin = await SignInAdminAsync();
        var email = $"personel-{Guid.NewGuid():N}@test.local";

        // Yönetici panelden Satış sorumlusu rolünde bir kullanıcı açar.
        var html = await Forms.PostAsync(admin, "/yonetim/kullanicilar/duzenle",
        [
            new("Input.FullName", "Deniz Personel"), new("Input.Email", email), new("Input.Password", "Personel.123"),
            new("Input.RoleIds", Role.SalesManagerId.ToString()), new("Input.IsActive", "true"), new("Input.IsActive", "false"),
        ]);
        Assert.Contains("Kullanıcı kaydedildi.", html);

        // Personel giriş yapar; kullanıcı yönetimine yetkisi yoktur.
        var staff = factory.CreateClient();
        await Forms.PostAsync(staff, "/yonetim/giris", [new("Input.Email", email), new("Input.Password", "Personel.123")]);
        Assert.Contains("/yonetim/yetkisiz", (await staff.GetAsync("/yonetim/kullanicilar")).RequestMessage!.RequestUri!.ToString());

        // Yönetici ona Yönetici rolünü de verir: personel yeniden giriş yapmadan sayfayı açabilir.
        var id = await IdOfAsync(email);
        await Forms.PostAsync(admin, $"/yonetim/kullanicilar/duzenle/{id}",
        [
            new("Input.FullName", "Deniz Personel"), new("Input.Email", email),
            new("Input.RoleIds", Role.SalesManagerId.ToString()), new("Input.RoleIds", Role.AdministratorId.ToString()),
            new("Input.IsActive", "true"), new("Input.IsActive", "false"),
        ]);
        var page = await staff.GetAsync("/yonetim/kullanicilar");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.EndsWith("/yonetim/kullanicilar", page.RequestMessage!.RequestUri!.ToString());

        // Yönetici hesabı pasif yapar: personelin açık oturumu hemen kapanır.
        await Forms.PostAsync(admin, $"/yonetim/kullanicilar/duzenle/{id}",
        [
            new("Input.FullName", "Deniz Personel"), new("Input.Email", email),
            new("Input.RoleIds", Role.SalesManagerId.ToString()), new("Input.IsActive", "false"),
        ]);
        Assert.Contains("/yonetim/giris", (await staff.GetAsync("/yonetim")).RequestMessage!.RequestUri!.ToString());
    }

    // ---- Yardımcılar ----

    private async Task<HttpClient> SignInAdminAsync()
    {
        var client = factory.CreateClient();
        var html = await Forms.PostAsync(client, "/yonetim/giris",
            [new("Input.Email", MySqlWebAppFactory.AdminEmail), new("Input.Password", MySqlWebAppFactory.AdminPassword)]);
        Assert.Contains("Hoş geldiniz", html);
        return client;
    }

    private async Task<int> IdOfAsync(string email)
    {
        var id = 0;
        await factory.WithDbAsync(async db => id = await db.StaffUsers
            .Where(u => u.OrganizationId == Organization.DefaultId && u.Email == PasswordService.NormalizeEmail(email))
            .Select(u => u.Id).SingleAsync());
        return id;
    }
}
