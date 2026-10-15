using System.Globalization;
using System.Net;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Localization;
using ConferenceRoom.Tests.Web;
using ConferenceRoom.Web.Auth;

namespace ConferenceRoom.Tests.Auth;

/// <summary>Veritabanı gerektirmeyen giriş testleri: sayfalar ayrı açılır, panel korunur, dönüş adresi güvenlidir.</summary>
public class AuthPageTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Theory]
    [InlineData("/yonetim/giris", "Yönetim girişi")]
    [InlineData("/giris", "Tekrar hoş geldiniz")]
    [InlineData("/kayit", "Hesap oluşturun")]
    public async Task Yonetim_ve_bilet_giris_sayfalari_ayri_ayri_acilir(string url, string title)
    {
        var html = await factory.CreateClient().GetStringAsync(url);
        Assert.Contains($"<h1>{title}</h1>", html);
    }

    [Fact]
    public async Task Yonetim_paneli_giris_yapmadan_acilmaz_yonetim_girisine_gonderir()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync("/yonetim");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/yonetim/giris", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("/yonetim/etkinlikler", "/yonetim", "/yonetim/etkinlikler")]
    [InlineData("https://kotu-site.com", "/yonetim", "/")]       // site dışına gidilmez
    [InlineData("//kotu-site.com", "/yonetim", "/")]
    [InlineData("/biletler", "/yonetim", "/")]                   // yönetim girişi yönetim dışına dönmez
    [InlineData("/yonetimx", "/yonetim", "/")]
    [InlineData("/biletler", null, "/biletler")]
    [InlineData(null, null, "/")]
    public void Donus_adresi_yalnizca_site_ici_ve_ilgili_bolumdedir(string? returnUrl, string? area, string expected)
    {
        Assert.Equal(expected, Principals.SafeReturnUrl(returnUrl, "/", area));
    }

    [Fact]
    public void Her_yetkinin_tum_dillerde_adi_ve_aciklamasi_vardir()
    {
        var catalog = new LanguageCatalog();
        foreach (var language in catalog.Languages)
            foreach (var permission in Permissions.All)
                foreach (var part in new[] { "name", "description" })
                {
                    var key = $"permission.{permission}.{part}";
                    Assert.True(catalog.Find(key, new CultureInfo(language.Culture)) is not null, $"{language.Culture}: {key} eksik");
                }
    }

    [Fact]
    public void Sifre_ozeti_dogru_sifreyi_kabul_eder_yanlisi_reddeder()
    {
        var passwords = new PasswordService();
        var hash = passwords.Hash("gizli-sifre");
        Assert.DoesNotContain("gizli-sifre", hash);
        Assert.True(passwords.Verify(hash, "gizli-sifre"));
        Assert.False(passwords.Verify(hash, "Gizli-sifre"));
        Assert.False(passwords.Verify(null, "gizli-sifre"));   // kullanıcı yoksa
    }
}
