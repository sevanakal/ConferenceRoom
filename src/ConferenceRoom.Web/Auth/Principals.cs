using System.Security.Claims;
using ConferenceRoom.Domain.Accounts;

namespace ConferenceRoom.Web.Auth;

/// <summary>
/// Giriş yapan kullanıcı için çereze yazılacak kimlik bilgisini (ClaimsPrincipal) üretir.
/// Yönetim kullanıcısının yetkileri girişte rollerinden toplanıp çereze yazılır; bu yüzden
/// rol yetkileri değiştiğinde kullanıcı yeniden giriş yapınca yeni yetkiler geçerli olur.
/// </summary>
public static class Principals
{
    /// <summary>Yönetim kullanıcısı. Roles → Role → Permissions yüklenmiş olmalı.</summary>
    public static ClaimsPrincipal ForStaff(StaffUser user)
    {
        var roles = user.Roles.Select(r => r.Role!).ToList();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r.Name)));
        claims.AddRange(roles.SelectMany(r => r.Permissions).Select(p => p.Permission).Distinct()
            .Select(p => new Claim(AppClaims.Permission, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.Staff));
    }

    /// <summary>Giriş yapmış kullanıcının (yönetim ya da izleyici) veritabanı kimliği. Yalnızca giriş isteyen sayfalarda çağrılır.</summary>
    public static int UserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Bilet alan izleyici.</summary>
    public static ClaimsPrincipal ForCustomer(Customer customer) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, customer.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Claim(ClaimTypes.Name, customer.FullName),
        new Claim(ClaimTypes.Email, customer.Email),
    ], AuthSchemes.Customer));

    /// <summary>
    /// Girişten sonra dönülecek adres. Yalnızca site içi adreslere izin verilir (açık yönlendirme olmasın);
    /// <paramref name="area"/> verilirse adres o bölümün içinde olmalıdır. Uymazsa varsayılan adrese gidilir.
    /// </summary>
    public static string SafeReturnUrl(string? returnUrl, string fallback, string? area = null)
    {
        var ok = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\");
        if (ok && area is not null)
            ok = returnUrl!.Equals(area, StringComparison.OrdinalIgnoreCase) || returnUrl!.StartsWith(area + "/", StringComparison.OrdinalIgnoreCase);
        return ok ? returnUrl! : fallback;
    }
}
