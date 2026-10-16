using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Tests.Auth;
using ConferenceRoom.Tests.Halls;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Events;

/// <summary>
/// Koltuk seçici ekranlarının (koltuk ayarları, satış yetkileri) testlerinde ortak yardımcılar:
/// krokili salon + etkinlik hazırlama ve ekranın yaptığı gibi "Save" işleyicisine veri gönderme.
/// </summary>
internal static partial class SeatScreens
{
    /// <summary>Doğrudan veritabanına krokili salon (koltuklar s1, s2, s3) ve etkinlik (istenirse bir seansla) ekler.</summary>
    public static async Task<(int EventId, int HallId)> CreateEventAsync(MySqlWebAppFactory factory, bool withSession)
    {
        (int, int) ids = default;
        await factory.WithDbAsync(async db =>
        {
            var adminId = await db.StaffUsers.Where(u => u.Email == MySqlWebAppFactory.AdminEmail).Select(u => u.Id).SingleAsync();
            var hall = new Hall { OrganizationId = Organization.DefaultId, Name = $"Salon {Guid.NewGuid():N}"[..20] };
            var version = new HallPlanVersion { Version = 1, PlanJson = HallPlanTests.SamplePlan, Capacity = 3, CreatedByStaffUserId = adminId };
            hall.PlanVersions.Add(version);
            var ev = new Event { OrganizationId = Organization.DefaultId, Title = "Koltuk testi", EventTypeId = EventType.ConcertId, Hall = hall, CreatedByStaffUserId = adminId };
            if (withSession)
                ev.Sessions.Add(new EventSession { StartsAtUtc = new DateTime(2030, 1, 1, 17, 0, 0), HallPlanVersion = version });
            db.Events.Add(ev);
            await db.SaveChangesAsync();
            ids = (ev.Id, hall.Id);
        });
        return ids;
    }

    /// <summary>Kullanıcı düzenleme sayfasından verilen rolde personel açar, onunla giriş yapar; (istemci, kullanıcı no) döner.</summary>
    public static async Task<(HttpClient Client, int Id)> CreateStaffAsync(MySqlWebAppFactory factory, HttpClient admin, int roleId, bool seatsRestricted = false)
    {
        var email = $"personel-{Guid.NewGuid():N}@test.local";
        await Forms.PostAsync(admin, "/yonetim/kullanicilar/duzenle",
        [
            new("Input.FullName", "Personel"), new("Input.Email", email), new("Input.Password", "Personel.12345"),
            new("Input.RoleIds", roleId.ToString()), new("Input.IsActive", "true"), new("Input.SeatsRestricted", seatsRestricted ? "true" : "false"),
        ]);
        var id = 0;
        await factory.WithDbAsync(async db => id = await db.StaffUsers.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return (await factory.SignInStaffAsync(email, "Personel.12345"), id);
    }

    /// <summary>Ekranın yaptığı gibi veriyi sayfanın Save işleyicisine gönderir; (durum, kayıt no) döner.</summary>
    public static async Task<(string Status, int Revision)> SaveAsync(HttpClient client, string pageUrl, string json, int baseRevision)
    {
        var page = await client.GetStringAsync(pageUrl);
        var config = JsonDocument.Parse(ConfigPattern().Match(page).Groups[1].Value).RootElement;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{pageUrl}?handler=Save&baseRevision={baseRevision}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("RequestVerificationToken", config.GetProperty("token").GetString());
        var response = await client.SendAsync(request);
        var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (answer.GetProperty("status").GetString()!, answer.GetProperty("revision").GetInt32());
    }

    [GeneratedRegex("<script type=\"application/json\" id=\"editorConfig\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex ConfigPattern();
}
