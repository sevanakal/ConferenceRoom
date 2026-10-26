using System.Security.Cryptography;
using System.Text;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Web.Auth;

/// <summary>
/// E-postayla gönderilen tek kullanımlık kodlar (<see cref="AccountToken"/>):
///   - şifre yenileme bağlantısı: uzun rastgele kod, 60 dakika geçerli (/sifre-yenile),
///   - giriş kodu (iki adımlı doğrulama): 6 hane, 10 dakika geçerli, en çok 5 yanlış deneme.
/// Kodların yalnızca SHA-256 özeti saklanır. Aynı hesap için yeni kod istenince eskileri geçersiz olur.
/// </summary>
public sealed class AccountCodes(AppDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan LoginCodeLifetime = TimeSpan.FromMinutes(10);
    public const int MaxLoginCodeAttempts = 5;

    public enum CodeResult { Ok, Wrong, Invalid }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---- Şifre yenileme ----

    /// <summary>Yeni şifre yenileme kodu üretir (bağlantıya konur); hesabın önceki yenileme kodları geçersiz olur.</summary>
    public async Task<string> CreateResetAsync(AccountKind kind, int accountId, CancellationToken cancellationToken)
    {
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await AddAsync(AccountTokenPurpose.PasswordReset, kind, accountId, code, ResetLifetime, cancellationToken);
        return code;
    }

    /// <summary>Kullanılabilir (kullanılmamış, süresi dolmamış) şifre yenileme kaydı; yoksa null.</summary>
    public Task<AccountToken?> FindResetAsync(string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 100)
            return Task.FromResult<AccountToken?>(null);
        var hash = HashOf(code);
        var now = Now;
        return db.AccountTokens.FirstOrDefaultAsync(t => t.CodeHash == hash && t.Purpose == AccountTokenPurpose.PasswordReset
            && t.UsedAtUtc == null && t.ExpiresAtUtc > now, cancellationToken);
    }

    /// <summary>Şifre değişince: hesabın bekleyen tüm kodları (yenileme ve giriş) geçersiz olur. Kayıt çağıranın SaveChanges'ıyla.</summary>
    public async Task InvalidateAllAsync(AccountKind kind, int accountId, CancellationToken cancellationToken)
    {
        foreach (var token in await PendingAsync(kind, accountId, null, cancellationToken))
            token.UsedAtUtc = Now;
    }

    // ---- Giriş kodu (iki adımlı doğrulama) ----

    /// <summary>Personel için yeni 6 haneli giriş kodu üretir; önceki giriş kodları geçersiz olur.</summary>
    public async Task<string> CreateLoginCodeAsync(int staffUserId, CancellationToken cancellationToken)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        await AddAsync(AccountTokenPurpose.LoginCode, AccountKind.Staff, staffUserId, code, LoginCodeLifetime, cancellationToken);
        return code;
    }

    /// <summary>
    /// Giriş kodunu denetler. Doğruysa kod kullanılmış sayılır. Yanlışsa deneme sayılır; 5. yanlışta kod geçersiz olur
    /// (Invalid: kod yok, süresi dolmuş ya da deneme hakkı bitmiş; yeni kod istenmeli).
    /// </summary>
    public async Task<CodeResult> VerifyLoginCodeAsync(int staffUserId, string? code, CancellationToken cancellationToken)
    {
        var token = (await PendingAsync(AccountKind.Staff, staffUserId, AccountTokenPurpose.LoginCode, cancellationToken))
            .Where(t => t.ExpiresAtUtc > Now).MaxBy(t => t.Id);
        if (token is null)
            return CodeResult.Invalid;

        var digits = new string((code ?? "").Where(char.IsAsciiDigit).ToArray());
        if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashOf(digits)), Encoding.ASCII.GetBytes(token.CodeHash)))
        {
            token.UsedAtUtc = Now;
            await db.SaveChangesAsync(cancellationToken);
            return CodeResult.Ok;
        }
        token.FailedAttempts++;
        if (token.FailedAttempts >= MaxLoginCodeAttempts)
            token.UsedAtUtc = Now;
        await db.SaveChangesAsync(cancellationToken);
        return token.UsedAtUtc is null ? CodeResult.Wrong : CodeResult.Invalid;
    }

    // ---- Yardımcılar ----

    private async Task AddAsync(AccountTokenPurpose purpose, AccountKind kind, int accountId, string code, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        foreach (var old in await PendingAsync(kind, accountId, purpose, cancellationToken))
            old.UsedAtUtc = Now;
        db.AccountTokens.Add(new AccountToken
        {
            OrganizationId = Organization.DefaultId,
            Purpose = purpose,
            AccountKind = kind,
            AccountId = accountId,
            CodeHash = HashOf(code),
            CreatedAtUtc = Now,
            ExpiresAtUtc = Now + lifetime,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<List<AccountToken>> PendingAsync(AccountKind kind, int accountId, AccountTokenPurpose? purpose, CancellationToken cancellationToken) =>
        db.AccountTokens.Where(t => t.AccountKind == kind && t.AccountId == accountId && t.UsedAtUtc == null
            && (purpose == null || t.Purpose == purpose)).ToListAsync(cancellationToken);

    private static string HashOf(string code) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
