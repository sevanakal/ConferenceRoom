using System.Globalization;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Localization;

/// <summary>
/// ASP.NET Core'un standart IStringLocalizer arayüzünü JSON dil dosyalarıyla uygular.
/// Sayfalarda kullanım:  @inject IStringLocalizer T   →   @T["home.title"]
/// Parametreli metin:    "Merhaba {0}"               →   @T["greeting", kullaniciAdi]
/// Dil, isteğin kültüründen (CultureInfo.CurrentUICulture) alınır.
/// </summary>
public sealed class JsonStringLocalizer(LanguageCatalog catalog) : IStringLocalizer
{
    public LocalizedString this[string name]
    {
        get
        {
            var value = catalog.Find(name, CultureInfo.CurrentUICulture);
            return new LocalizedString(name, value ?? name, resourceNotFound: value is null);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var template = this[name];
            return new LocalizedString(name, string.Format(CultureInfo.CurrentCulture, template.Value, arguments), template.ResourceNotFound);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        catalog.GetAll(CultureInfo.CurrentUICulture).Select(kv => new LocalizedString(kv.Key, kv.Value));
}

/// <summary>
/// Tüm sınıflar aynı dil dosyalarını paylaşır; bu yüzden fabrika hangi tür için istenirse istensin aynı localizer'ı verir.
/// </summary>
public sealed class JsonStringLocalizerFactory(LanguageCatalog catalog) : IStringLocalizerFactory
{
    private readonly JsonStringLocalizer _localizer = new(catalog);

    public IStringLocalizer Create(Type resourceSource) => _localizer;

    public IStringLocalizer Create(string baseName, string location) => _localizer;
}
