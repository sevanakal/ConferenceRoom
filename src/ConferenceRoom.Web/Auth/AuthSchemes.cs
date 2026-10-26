namespace ConferenceRoom.Web.Auth;

/// <summary>
/// Oturum türleri; her birinin kendi çerezi vardır.
///   Staff:    yönetim paneli, giriş /yonetim/giris. Yalnızca panel yetkisi olan personel (satış dışında en az bir yetki).
///   Sales:    satış personeli, giriş /giris (izleyiciyle aynı sayfa). Yalnızca "sales.sell" yetkisi olan personel;
///             panel açılmaz, çerez yalnızca /satis altına gönderilir.
///   Customer: bilet alan izleyici, giriş /giris.
///   TwoFactor: iki adımlı doğrulamada kodu bekleyen personel (/dogrulama), oturum değildir.
/// Yönetim çerezi yalnızca /yonetim altına gönderilir; bir girişten alınan oturum diğer bölümde geçmez.
/// </summary>
public static class AuthSchemes
{
    /// <summary>Yönetim paneli (StaffUser). Giriş: /yonetim/giris</summary>
    public const string Staff = "Yonetim";

    /// <summary>Satış personeli (StaffUser, "sales.sell" yetkili). Giriş: /giris. Bölüm: /satis</summary>
    public const string Sales = "Satis";

    /// <summary>Bilet alan izleyici (Customer). Giriş: /giris. Genel sayfalarda varsayılan şema budur.</summary>
    public const string Customer = "Uye";

    /// <summary>
    /// İki adımlı doğrulamada şifresi doğrulanmış, kodu bekleyen personel (kısa ömürlü; yalnızca /dogrulama sayfası okur).
    /// Bkz. <see cref="TwoFactorLogin"/>.
    /// </summary>
    public const string TwoFactor = "IkiAdim";
}

/// <summary>Yetkilendirme kuralı (policy) adları.</summary>
public static class AuthPolicies
{
    /// <summary>Yönetim paneline giriş yapmış herhangi bir kullanıcı.</summary>
    public const string Staff = "Yonetim";

    /// <summary>Satış bölümüne (/satis) giriş yapmış, satış yetkisi olan personel.</summary>
    public const string Sales = "Satis";

    /// <summary>Belirli bir yetki isteyen kuralın adı, ör. Permission("halls.manage") → "Yetki:halls.manage".</summary>
    public static string Permission(string permission) => "Yetki:" + permission;
}

/// <summary>Çerezdeki kimlik bilgisinde kullanılan alan (claim) türleri.</summary>
public static class AppClaims
{
    /// <summary>Kullanıcının rollerinden gelen her yetki ayrı bir "permission" alanı olarak yazılır.</summary>
    public const string Permission = "permission";
}
