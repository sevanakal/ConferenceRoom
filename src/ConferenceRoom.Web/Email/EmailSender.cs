using ConferenceRoom.Domain.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ConferenceRoom.Web.Email;

/// <summary>Gönderilecek düz metin e-posta.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>
/// E-posta gönderimi. Uygulamada <see cref="SmtpEmailSender"/> (panelden yapılan SMTP ayarlarıyla); testlerde yerine
/// gönderilenleri biriktiren bir sınıf konur.
/// </summary>
public interface IEmailSender
{
    /// <summary>Gönderir; ayar yoksa <see cref="EmailNotConfiguredException"/>, bağlantı/kimlik hatasında <see cref="EmailSendException"/>.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>SMTP ayarları henüz yapılmamış.</summary>
public sealed class EmailNotConfiguredException() : Exception("SMTP ayarları yapılmamış.");

/// <summary>SMTP sunucusuna bağlanılamadı ya da sunucu e-postayı reddetti; ayrıntı günlüğe yazılır.</summary>
public sealed class EmailSendException(string message, Exception inner) : Exception(message, inner);

/// <summary>MailKit ile SMTP üzerinden gönderim. Ayarlar her gönderimde veritabanından okunur (panelde değişince hemen geçerli).</summary>
public sealed class SmtpEmailSender(SmtpSettingsStore store, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    /// <summary>Sunucu yanıt vermezse beklenecek en uzun süre.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = await store.FindAsync(cancellationToken) ?? throw new EmailNotConfiguredException();

        // ---- İleti ----
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName ?? "", settings.FromEmail));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        // ---- Gönderim ----
        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, OptionsOf(settings.Security), cancellationToken);
            if (!string.IsNullOrEmpty(settings.UserName))
                await client.AuthenticateAsync(settings.UserName, store.PasswordOf(settings) ?? "", cancellationToken);
            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "E-posta gönderilemedi: {Host}:{Port}", settings.Host, settings.Port);
            throw new EmailSendException(ex.Message, ex);
        }
    }

    private static SecureSocketOptions OptionsOf(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.StartTls,
    };
}
