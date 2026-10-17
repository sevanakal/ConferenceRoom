using System.Net;
using System.Net.Http.Json;

namespace ConferenceRoom.Tests.Web;

/// <summary>Sayfaların açıldığını, dil seçiminin çalıştığını ve dil API'sini doğrular.</summary>
public class PageAndApiTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task Ana_sayfa_etkinlik_panosu_olarak_varsayilan_turkce_acilir()
    {
        var html = await factory.CreateClient().GetStringAsync("/");
        Assert.Contains("<html lang=\"tr\">", html);
        Assert.Contains("Etkinlik panosu", html);
        // Veritabanı yokken sayfa çökmez, uyarı gösterir.
        Assert.Contains("Etkinlik bilgilerine şu anda ulaşılamıyor", html);
    }

    [Fact]
    public async Task Adres_satirindaki_culture_parametresi_dili_degistirir()
    {
        var html = await factory.CreateClient().GetStringAsync("/?culture=en");
        Assert.Contains("Event board", html);
    }

    [Fact]
    public async Task Dil_secici_cerez_yazar_ve_geri_doner()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync("/language/en?returnUrl=%2F");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.Contains("c%3Den"));
    }

    [Fact]
    public async Task Dil_secici_site_disina_yonlendirmez()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync("/language/en?returnUrl=https%3A%2F%2Fornek.com");
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Dil_api_si_metinleri_doner()
    {
        var client = factory.CreateClient();
        var languages = await client.GetFromJsonAsync<List<LanguageDto>>("/api/languages");
        Assert.Contains(languages!, l => l.Culture == "en" && l.Name == "English");

        var strings = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/languages/en");
        Assert.Equal("Event board", strings!["board.title"]);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/languages/xx")).StatusCode);
    }

    private sealed record LanguageDto(string Culture, string Name);
}
