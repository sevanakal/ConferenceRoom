using System.Globalization;

namespace ConferenceRoom.Web.Common;

/// <summary>
/// Veritabanındaki UTC zamanları ekranda Türkiye saatiyle gösterir. Sunucu (Docker) UTC'de çalıştığı için
/// dönüşüm açıkça yapılır. Çok kuruma geçildiğinde saat dilimi kurum ayarına taşınabilir.
/// </summary>
public static class DisplayTime
{
    private static readonly TimeZoneInfo Zone = FindZone();

    /// <summary>Ör. "15.10.2026 19:42" (Türkçe) / "15/10/2026 19:42" (İngilizce): geçerli dilin kısa tarih biçimi.</summary>
    public static string Format(DateTime utc) => ToLocal(utc).ToString("g", CultureInfo.CurrentCulture);

    /// <summary>UTC zamanı Türkiye saatine çevirir (formlarda varsayılan değer, panoda gün başlıkları için).</summary>
    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>Formdan gelen Türkiye saatini veritabanında saklanacak UTC zamana çevirir.</summary>
    public static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    private static TimeZoneInfo FindZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("TRT", TimeSpan.FromHours(3), "TRT", "TRT");   // saat dilimi verisi olmayan sistemler için
        }
    }
}
