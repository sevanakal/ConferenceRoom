using System.Globalization;
using ConferenceRoom.Localization;
using Microsoft.AspNetCore.Localization;

namespace ConferenceRoom.Web.Api;

/// <summary>
/// Dil ile ilgili adresler:
///   GET /api/languages              → kullanılabilir diller
///   GET /api/languages/{kültür}     → o dilin tüm metinleri (mobil uygulama ve JavaScript için)
///   GET /language/{kültür}?returnUrl=/  → dil seçimini çereze yazar ve sayfaya geri döner (dil seçici bunu kullanır)
/// </summary>
public static class LanguageEndpoints
{
    public static IEndpointRouteBuilder MapLanguageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/languages", (LanguageCatalog catalog) => catalog.Languages);

        app.MapGet("/api/languages/{culture}", (string culture, LanguageCatalog catalog) =>
            catalog.IsSupported(culture)
                ? Results.Ok(catalog.GetAll(new CultureInfo(culture)))
                : Results.NotFound());

        app.MapGet("/language/{culture}", (string culture, string? returnUrl, LanguageCatalog catalog, HttpContext http) =>
        {
            if (catalog.IsSupported(culture))
            {
                http.Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = true, SameSite = SameSiteMode.Lax });
            }
            // Yalnızca site içi adreslere dön (açık yönlendirme açığını önler).
            return Results.LocalRedirect(returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : "/");
        });

        return app;
    }
}
