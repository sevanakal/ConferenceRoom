using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Localization;

public static class LocalizationServiceCollectionExtensions
{
    /// <summary>
    /// JSON tabanlı dil desteğini ekler. ASP.NET Core'un varsayılan (.resx) localizer'ının yerine geçer,
    /// bu yüzden IStringLocalizer ve IStringLocalizer&lt;T&gt; isteyen her yer bu dosyaları kullanır.
    /// </summary>
    public static IServiceCollection AddJsonLocalization(this IServiceCollection services)
    {
        services.TryAddSingleton<LanguageCatalog>();
        services.RemoveAll<IStringLocalizerFactory>();
        services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
        services.TryAddTransient(typeof(IStringLocalizer<>), typeof(StringLocalizer<>));
        services.TryAddSingleton<IStringLocalizer>(sp => sp.GetRequiredService<IStringLocalizerFactory>().Create(typeof(LanguageCatalog)));
        return services;
    }
}
