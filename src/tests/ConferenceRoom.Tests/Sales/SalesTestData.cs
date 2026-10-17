using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Tests.Auth;
using ConferenceRoom.Tests.Events;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Sales;

/// <summary>
/// Satış testlerinin ortak verisi ve yardımcıları (satış ekranı, bildirimler, satış raporu).
/// Örnek krokide koltuklar s1 (A-1), s2 (A-2), s3 (EK-1); <see cref="Map"/> ile s1 500 TL, s2 ücretsiz, s3 protokol.
/// </summary>
internal static partial class SalesTestData
{
    public const string Map = """
        { "schema": 1,
          "categories": [
            { "id": "c1", "name": "1. Kategori", "price": 500, "color": "#1D5FBF", "protocol": false },
            { "id": "c0", "name": "Davetli", "price": 0, "color": "#0F766E", "protocol": false },
            { "id": "cp", "name": "Protokol", "price": 0, "color": "#6D3FC0", "protocol": true } ],
          "seats": { "s1": "c1", "s2": "c0", "s3": "cp" },
          "online": [] }
        """;

    /// <summary>Yayında, verilen satış türünde, koltuk ayarları yapılmış bir etkinlik ve seansı açar; seans no döner.</summary>
    public static async Task<int> CreateSessionAsync(MySqlWebAppFactory factory, SaleMode mode, DateTime? salesStartUtc = null)
    {
        var (eventId, _) = await SeatScreens.CreateEventAsync(factory, withSession: true);
        var sessionId = 0;
        await factory.WithDbAsync(async db =>
        {
            var ev = await db.Events.SingleAsync(e => e.Id == eventId);
            ev.Status = EventStatus.Published;
            ev.SaleMode = mode;
            ev.SalesStartUtc = salesStartUtc;
            db.EventSeatMaps.Add(new EventSeatMap { OrganizationId = Organization.DefaultId, EventId = eventId, MapJson = Map, Revision = 1, UpdatedByStaffUserId = ev.CreatedByStaffUserId });
            await db.SaveChangesAsync();
            sessionId = await db.EventSessions.Where(s => s.EventId == eventId).Select(s => s.Id).SingleAsync();
        });
        return sessionId;
    }

    /// <summary>Satış ekranının yaptığı gibi satış isteğini gönderir; (durum, cevap) döner.</summary>
    public static async Task<(string Status, JsonElement Answer)> SellAsync(HttpClient client, int sessionId, object body)
    {
        var url = $"/satis/seans/{sessionId}";
        var page = await client.GetStringAsync(url);
        var config = JsonDocument.Parse(ConfigPattern().Match(page).Groups[1].Value).RootElement;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}?handler=Sell") { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", config.GetProperty("token").GetString());
        var response = await client.SendAsync(request);
        var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (answer.GetProperty("status").GetString()!, answer);
    }

    [GeneratedRegex("<script type=\"application/json\" id=\"editorConfig\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex ConfigPattern();
}
