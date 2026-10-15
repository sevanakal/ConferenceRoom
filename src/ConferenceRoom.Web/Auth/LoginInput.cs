namespace ConferenceRoom.Web.Auth;

/// <summary>Giriş formunun alanları (yönetim ve izleyici girişi aynı alanları kullanır).</summary>
public sealed class LoginInput
{
    public string? Email { get; set; }

    public string? Password { get; set; }

    public bool RememberMe { get; set; }
}
