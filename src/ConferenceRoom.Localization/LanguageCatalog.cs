using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace ConferenceRoom.Localization;

/// <summary>
/// Tüm dil dosyalarını (Resources/*.json) belleğe yükler ve metinleri anahtarla verir.
///
/// Dosya biçimi: iç içe JSON. Anahtarlar noktayla birleştirilir:
///   { "home": { "title": "Hoş geldiniz" } }  →  "home.title"
/// "_language" bölümü dilin kendi adını taşır (dil seçicide gösterilir), metin olarak kullanılmaz.
///
/// Arama sırası: istenen kültür ("en-US") → üst kültür ("en") → varsayılan dil ("tr").
/// Hiçbirinde yoksa null döner; çağıran taraf anahtarın kendisini gösterir, böylece eksik çeviri ekranda hemen fark edilir.
/// </summary>
public sealed class LanguageCatalog
{
    /// <summary>Varsayılan dil. Diğer dillerde eksik olan metinler bu dilden gelir.</summary>
    public const string DefaultCulture = "tr";

    private const string ResourcePrefix = "ConferenceRoom.Localization.";
    private const string MetaSection = "_language";

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _strings;

    public LanguageCatalog()
    {
        _strings = new(StringComparer.OrdinalIgnoreCase);
        var languages = new List<LanguageInfo>();
        var assembly = typeof(LanguageCatalog).Assembly;

        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix) && n.EndsWith(".json")))
        {
            var culture = resource[ResourcePrefix.Length..^".json".Length];
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var json = JsonDocument.Parse(stream);

            var flat = new Dictionary<string, string>(StringComparer.Ordinal);
            Flatten(json.RootElement, prefix: null, flat);

            _strings[culture] = flat;
            languages.Add(new LanguageInfo(culture, flat.GetValueOrDefault($"{MetaSection}.name", culture)));
        }

        if (!_strings.ContainsKey(DefaultCulture))
            throw new InvalidOperationException($"Varsayılan dil dosyası bulunamadı: Resources/{DefaultCulture}.json");

        // Varsayılan dil listede ilk sırada, diğerleri ada göre.
        Languages = languages.OrderBy(l => l.Culture == DefaultCulture ? 0 : 1).ThenBy(l => l.Name).ToList();
    }

    /// <summary>Kullanılabilir diller (dil seçicide ve istek dili ayarında kullanılır).</summary>
    public IReadOnlyList<LanguageInfo> Languages { get; }

    /// <summary>Bir metni verilen kültür için bulur; bulamazsa üst kültüre ve varsayılan dile bakar.</summary>
    public string? Find(string key, CultureInfo culture)
    {
        foreach (var name in FallbackChain(culture))
        {
            if (_strings.TryGetValue(name, out var table) && table.TryGetValue(key, out var value))
                return value;
        }
        return null;
    }

    /// <summary>
    /// Bir dilin tüm metinleri (eksikleri varsayılan dilden tamamlanmış hâlde).
    /// Mobil uygulama ve tarayıcıdaki JavaScript bu listeyi API'den alır.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAll(CultureInfo culture)
    {
        var result = new Dictionary<string, string>(_strings[DefaultCulture], StringComparer.Ordinal);
        foreach (var name in FallbackChain(culture).Reverse())
        {
            if (_strings.TryGetValue(name, out var table))
                foreach (var (key, value) in table) result[key] = value;
        }
        foreach (var key in result.Keys.Where(k => k.StartsWith(MetaSection + ".")).ToList())
            result.Remove(key);
        return result;
    }

    /// <summary>Verilen kültürün desteklenip desteklenmediği (ör. "en" ya da "en-GB" → en.json varsa true).</summary>
    public bool IsSupported(string culture) =>
        _strings.ContainsKey(culture) || (culture.Contains('-') && _strings.ContainsKey(culture.Split('-')[0]));

    /// <summary>Testler için: bir dil dosyasındaki anahtarların listesi.</summary>
    public IEnumerable<string> KeysOf(string culture) => _strings[culture].Keys;

    private static IEnumerable<string> FallbackChain(CultureInfo culture)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
            if (seen.Add(c.Name)) yield return c.Name;
        if (seen.Add(DefaultCulture)) yield return DefaultCulture;
    }

    private static void Flatten(JsonElement element, string? prefix, Dictionary<string, string> into)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                Flatten(property.Value, prefix is null ? property.Name : $"{prefix}.{property.Name}", into);
        }
        else if (prefix is not null)
        {
            into[prefix] = element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
        }
    }
}

/// <summary>Bir dilin kodu (ör. "tr") ve kendi dilindeki adı (ör. "Türkçe").</summary>
public sealed record LanguageInfo(string Culture, string Name);
