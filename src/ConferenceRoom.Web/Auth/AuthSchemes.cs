namespace ConferenceRoom.Web.Auth;

/// <summary>
/// İki ayrı giriş: yönetim paneli ve bilet alan izleyici. Her birinin kendi çerezi ve giriş sayfası vardır;
/// kullanıcı türüne göre otomatik yönlendirme yapılmaz. Yönetim çerezi yalnızca /yonetim altına gönderilir.
/// </summary>
public static class AuthSchemes
{
    /// <summary>Yönetim paneli (StaffUser). Giriş: /yonetim/giris</summary>
    public const string Staff = "Yonetim";

    /// <summary>Bilet alan izleyici (Customer). Giriş: /giris. Genel sayfalarda varsayılan şema budur.</summary>
    public const string Customer = "Uye";
}

/// <summary>Yetkilendirme kuralı (policy) adları.</summary>
public static class AuthPolicies
{
    /// <summary>Yönetim paneline giriş yapmış herhangi bir kullanıcı.</summary>
    public const string Staff = "Yonetim";

    /// <summary>Belirli bir yetki isteyen kuralın adı, ör. Permission("halls.manage") → "Yetki:halls.manage".</summary>
    public static string Permission(string permission) => "Yetki:" + permission;
}

/// <summary>Çerezdeki kimlik bilgisinde kullanılan alan (claim) türleri.</summary>
public static class AppClaims
{
    /// <summary>Kullanıcının rollerinden gelen her yetki ayrı bir "permission" alanı olarak yazılır.</summary>
    public const string Permission = "permission";
}
