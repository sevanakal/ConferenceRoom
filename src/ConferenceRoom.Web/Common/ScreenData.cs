using System.Globalization;
using ConferenceRoom.Localization;

namespace ConferenceRoom.Web.Common;

/// <summary>
/// Tarayıcıda çalışan ekranlara (kroki editörü, koltuk seçici ekranları) sayfa içinde JSON olarak verilen bilgilerin yardımcıları.
/// Ekranlar verilerini &lt;script type="application/json"&gt; bloklarından okur.
/// </summary>
public static class ScreenData
{
    /// <summary>
    /// Dil dosyasının verilen bölümlerindeki metinler, bölüm öneki atılarak ("seatEditor.save" → "save").
    /// Aynı anahtar birden çok bölümde varsa sonraki bölüm kazanır (ekrana özgü bölüm ortak bölümün metnini değiştirebilir).
    /// </summary>
    public static Dictionary<string, string> Texts(LanguageCatalog catalog, params string[] sections)
    {
        var all = catalog.GetAll(CultureInfo.CurrentUICulture);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            var prefix = section + ".";
            foreach (var (key, value) in all.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)))
                texts[key[prefix.Length..]] = value;
        }
        return texts;
    }

    /// <summary>JSON'un &lt;script&gt; bloğu içinde "&lt;/script&gt;" ile sayfayı kırmaması için.</summary>
    public static string ForScript(string json) => json.Replace("</", "<\\/", StringComparison.Ordinal);
}
