using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Tests.Database;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Auth;

/// <summary>
/// SMTP ayarları ve şifre yenileme (gerçek MySQL; e-postalar TestEmailSender'da birikir): ayarlar panelden kaydedilir,
/// şifre şifreli saklanır; "şifremi unuttum" kayıtlı adrese tek kullanımlık bağlantı gönderir, kayıtsız adrese bir şey
/// göndermez ama aynı cevabı verir.
/// </summary>
[Collection(MySqlCollection.Name)]
public class PasswordResetTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Smtp_ayarlari_panelden_kaydedilir_sifre_sifreli_saklanir_ve_deneme_epostasi_gider()
    {
        var admin = await factory.SignInStaffAsync();
        var html = await Forms.PostAsync(admin, "/yonetim/ayarlar",
        [
            new("Input.Host", "smtp.kurum.test"), new("Input.Port", "587"), new("Input.Security", "StartTls"),
            new("Input.UserName", "bilet@kurum.test"), new("Input.Password", "Gizli.Smtp.Sifresi"),
            new("Input.FromEmail", "bilet@kurum.test"), new("Input.FromName", "Kurum"), new("Input.PublicUrl", "https://bilet.kurum.test/"),
        ]);
        Assert.Contains("Ayarlar kaydedildi.", html);
        Assert.DoesNotContain("Gizli.Smtp.Sifresi", html);

        await factory.WithDbAsync(async db =>
        {
            var settings = await db.SmtpSettings.SingleAsync();
            Assert.Equal("https://bilet.kurum.test", settings.PublicUrl);   // sondaki "/" atılır
            Assert.NotNull(settings.ProtectedPassword);
            Assert.DoesNotContain("Gizli.Smtp.Sifresi", settings.ProtectedPassword);
        });

        var sent = await Forms.PostAsync(admin, "/yonetim/ayarlar", [new("TestTo", "deneme@kurum.test")], postUrl: "/yonetim/ayarlar?handler=Test");
        Assert.Contains("Deneme e-postası deneme@kurum.test adresine gönderildi.", sent);
        Assert.Single(factory.Emails.To("deneme@kurum.test"));
    }

    [MySqlFact]
    public async Task Sifremi_unuttum_baglantisiyla_sifre_bir_kez_yenilenir_ve_yeni_sifreyle_girilir()
    {
        await factory.ConfigureSmtpAsync();
        var (email, _) = await factory.CreateStaffAsync([Permissions.HallsManage]);
        var client = factory.CreateClient();

        var html = await Forms.PostAsync(client, "/sifremi-unuttum", [new("Email", email.ToUpperInvariant())]);
        Assert.Contains("şifre yenileme bağlantısı gönderildi", html);
        var path = factory.Emails.LastLinkPath(email, await PublicUrlAsync());

        var done = await Forms.PostAsync(client, path, [new("Password", "Yeni.Sifre.2026"), new("PasswordConfirm", "Yeni.Sifre.2026")]);
        Assert.Contains("Şifreniz değiştirildi.", done);
        Assert.Contains("href=\"/yonetim/giris\"", done);

        // Eski şifre geçmez, yenisi geçer; bağlantı ikinci kez kullanılamaz.
        var old = await Forms.PostAsync(factory.CreateClient(), "/yonetim/giris", [new("Input.Email", email), new("Input.Password", MySqlWebAppFactory.StaffPassword)]);
        Assert.Contains("E-posta ya da şifre hatalı.", old);
        await factory.SignInStaffAsync(email, "Yeni.Sifre.2026");
        Assert.Contains("Bu bağlantı geçersiz ya da süresi dolmuş.", await client.GetStringAsync(path));
    }

    [MySqlFact]
    public async Task Kayitli_olmayan_adrese_eposta_gitmez_ama_ayni_cevap_verilir()
    {
        await factory.ConfigureSmtpAsync();
        var html = await Forms.PostAsync(factory.CreateClient(), "/sifremi-unuttum", [new("Email", "kimse@test.local")]);
        Assert.Contains("şifre yenileme bağlantısı gönderildi", html);
        Assert.Empty(factory.Emails.To("kimse@test.local"));
    }

    [MySqlFact]
    public async Task Sifreler_ayni_degilse_ya_da_kisaysa_sifre_degismez()
    {
        await factory.ConfigureSmtpAsync();
        var (email, _) = await factory.CreateStaffAsync([Permissions.HallsManage]);
        var client = factory.CreateClient();
        await Forms.PostAsync(client, "/sifremi-unuttum", [new("Email", email)]);
        var path = factory.Emails.LastLinkPath(email, await PublicUrlAsync());

        Assert.Contains("Şifreler aynı değil.", await Forms.PostAsync(client, path, [new("Password", "Yeni.Sifre.2026"), new("PasswordConfirm", "Baska.Sifre.2026")]));
        Assert.Contains("Şifre en az 8 karakter olmalı.", await Forms.PostAsync(client, path, [new("Password", "kisa"), new("PasswordConfirm", "kisa")]));
        await factory.SignInStaffAsync(email, MySqlWebAppFactory.StaffPassword);
    }

    private async Task<string> PublicUrlAsync()
    {
        var url = "";
        await factory.WithDbAsync(async db => url = await db.SmtpSettings.Select(s => s.PublicUrl).SingleAsync());
        return url;
    }
}
