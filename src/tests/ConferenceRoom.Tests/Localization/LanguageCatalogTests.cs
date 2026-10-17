using System.Globalization;
using ConferenceRoom.Localization;

namespace ConferenceRoom.Tests.Localization;

/// <summary>Dil dosyalarının tutarlılığı ve metin arama sırası.</summary>
public class LanguageCatalogTests
{
    private readonly LanguageCatalog _catalog = new();

    [Fact]
    public void Turkce_varsayilan_dildir_ve_listede_ilk_siradadir()
    {
        Assert.Equal("tr", _catalog.Languages[0].Culture);
        Assert.Equal("Türkçe", _catalog.Languages[0].Name);
    }

    /// <summary>
    /// Her dil dosyası Türkçe dosyadaki anahtarların tamamını içermeli ve fazladan anahtar taşımamalı.
    /// Yeni bir metin eklenip bir dilde unutulursa bu test hangi anahtarın eksik olduğunu söyler.
    /// </summary>
    [Fact]
    public void Tum_dil_dosyalari_ayni_anahtarlara_sahiptir()
    {
        var reference = _catalog.KeysOf(LanguageCatalog.DefaultCulture).ToHashSet();
        foreach (var language in _catalog.Languages.Where(l => l.Culture != LanguageCatalog.DefaultCulture))
        {
            var keys = _catalog.KeysOf(language.Culture).ToHashSet();
            Assert.True(reference.SetEquals(keys),
                $"{language.Culture}.json eksik: [{string.Join(", ", reference.Except(keys))}] fazla: [{string.Join(", ", keys.Except(reference))}]");
        }
    }

    [Theory]
    [InlineData("tr", "Etkinlik panosu")]
    [InlineData("en", "Event board")]
    [InlineData("en-GB", "Event board")]     // üst kültüre (en) düşer
    [InlineData("de", "Etkinlik panosu")]   // olmayan dil varsayılana (tr) düşer
    public void Metin_kultur_sirasina_gore_bulunur(string culture, string expected)
    {
        Assert.Equal(expected, _catalog.Find("board.title", new CultureInfo(culture)));
    }

    [Fact]
    public void Olmayan_anahtar_icin_null_doner()
    {
        Assert.Null(_catalog.Find("yok.boyle.bir.anahtar", new CultureInfo("tr")));
    }

    [Fact]
    public void GetAll_dil_bilgisi_bolumunu_icermez()
    {
        var all = _catalog.GetAll(new CultureInfo("en"));
        Assert.Equal("Event board", all["board.title"]);
        Assert.DoesNotContain(all.Keys, k => k.StartsWith("_language"));
    }
}
