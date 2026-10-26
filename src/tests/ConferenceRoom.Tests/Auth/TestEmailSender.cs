using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ConferenceRoom.Web.Email;

namespace ConferenceRoom.Tests.Auth;

/// <summary>
/// Testlerde gerçek SMTP yerine kullanılan e-posta gönderici: gönderilenleri biriktirir (MySqlWebAppFactory.Emails).
/// Uygulamadaki gibi SMTP ayarı yoksa gönderimi reddeder ki "ayar yapılmamış" durumları da sınanabilsin.
/// </summary>
public sealed partial class TestEmailSender(Func<Task<bool>> isConfigured) : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (!await isConfigured())
            throw new EmailNotConfiguredException();
        _sent.Enqueue(message);
    }

    /// <summary>Bu adrese gönderilen e-postalar, eskiden yeniye.</summary>
    public IReadOnlyList<EmailMessage> To(string address) => [.. _sent.Where(m => m.To == address)];

    /// <summary>Bu adrese giden son e-postadaki 6 haneli kod.</summary>
    public string LastCode(string address) => CodePattern().Match(To(address)[^1].Body).Value;

    /// <summary>Bu adrese giden son e-postadaki bağlantının site içi yolu (ör. "/sifre-yenile?kod=...").</summary>
    public string LastLinkPath(string address, string publicUrl)
    {
        var link = LinkPattern().Match(To(address)[^1].Body).Value;
        Assert.StartsWith(publicUrl + "/", link);
        return link[publicUrl.Length..];
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex LinkPattern();
}
