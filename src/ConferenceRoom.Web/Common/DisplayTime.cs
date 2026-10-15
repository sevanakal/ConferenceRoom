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
    public static string Format(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone).ToString("g", CultureInfo.CurrentCulture);

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
