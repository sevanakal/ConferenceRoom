using System.Text.RegularExpressions;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Halls;
using ConferenceRoom.Domain.Organizations;
using ConferenceRoom.Tests.Auth;
using ConferenceRoom.Tests.Database;
using ConferenceRoom.Tests.Halls;
using Microsoft.EntityFrameworkCore;

namespace ConferenceRoom.Tests.Events;

/// <summary>
/// Etkinlikler ve seanslar (gerçek MySQL): etkinliğin taslak kaydı, seansın salonun güncel kroki sürümüne bağlanması,
/// yayınlama kuralları, afiş denetimi, herkese açık pano ve yetki kontrolü.
/// </summary>
[Collection(MySqlCollection.Name)]
public partial class EventManagementTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Yeni_etkinlik_taslak_kaydedilir_ve_seans_salonun_guncel_kroki_surumune_baglanir()
    {
        var admin = await factory.SignInStaffAsync();
        var hallId = await CreateHallAsync(planVersions: 2);

        var eventId = await CreateEventAsync(admin, hallId);
        await AddSessionAsync(admin, eventId, hallId, "2030-05-20", "20:00");

        await factory.WithDbAsync(async db =>
        {
            var ev = await db.Events.Include(e => e.Sessions).ThenInclude(s => s.HallPlanVersion).SingleAsync(e => e.Id == eventId);
            Assert.Equal(EventStatus.Draft, ev.Status);
            var session = Assert.Single(ev.Sessions);
            Assert.Equal(2, session.HallPlanVersion!.Version);
            Assert.Equal(new DateTime(2030, 5, 20, 17, 0, 0), session.StartsAtUtc);   // 20:00 Türkiye saati = 17:00 UTC
        });
    }

    [MySqlFact]
    public async Task Krokisi_olmayan_salonun_etkinligine_seans_eklenemez()
    {
        var admin = await factory.SignInStaffAsync();
        var hallId = await CreateHallAsync(planVersions: 0);
        var eventId = await CreateEventAsync(admin, hallId);

        var html = await AddSessionAsync(admin, eventId, hallId, "2030-05-20", "20:00");

        Assert.Contains("henüz krokisi yok", html);
        await factory.WithDbAsync(async db => Assert.False(await db.EventSessions.AnyAsync(s => s.EventId == eventId)));
    }

    [MySqlFact]
    public async Task Seansi_olmayan_etkinlik_yayinlanamaz()
    {
        var admin = await factory.SignInStaffAsync();
        var hallId = await CreateHallAsync(planVersions: 1);
        var eventId = await CreateEventAsync(admin, hallId);

        var html = await SaveEventAsync(admin, eventId, hallId, "Published");

        Assert.Contains("en az bir seans", html);
        await factory.WithDbAsync(async db => Assert.Equal(EventStatus.Draft, (await db.Events.SingleAsync(e => e.Id == eventId)).Status));
    }

    [MySqlFact]
    public async Task Pano_giris_istemez_yayindaki_ve_iptal_edilen_etkinlikleri_gosterir_taslaklari_gostermez()
    {
        var admin = await factory.SignInStaffAsync();
        var hallId = await CreateHallAsync(planVersions: 1);
        var published = await CreateEventAsync(admin, hallId);
        var cancelled = await CreateEventAsync(admin, hallId);
        var draft = await CreateEventAsync(admin, hallId);
        foreach (var id in new[] { published, cancelled, draft })
            await AddSessionAsync(admin, id, hallId, "2030-06-01", "19:30");
        await SaveEventAsync(admin, published, hallId, "Published");
        await SaveEventAsync(admin, cancelled, hallId, "Cancelled");

        var board = await factory.CreateClient().GetStringAsync("/pano");

        Assert.Contains(TitleOf(published), board);
        Assert.Contains(TitleOf(cancelled), board);
        Assert.Contains("İptal edildi", board);
        Assert.DoesNotContain(TitleOf(draft), board);
    }

    [MySqlFact]
    public async Task Ana_sayfa_panodur_satistaki_seansin_bilet_al_dugmesi_bilet_sayfasini_acar_iptal_ve_taslakta_acmaz()
    {
        var admin = await factory.SignInStaffAsync();
        var hallId = await CreateHallAsync(planVersions: 1);
        var onSale = await CreateEventAsync(admin, hallId);
        var cancelled = await CreateEventAsync(admin, hallId);
        var draft = await CreateEventAsync(admin, hallId);
        foreach (var id in new[] { onSale, cancelled, draft })
            await AddSessionAsync(admin, id, hallId, "2030-07-01", "20:00");
        await SaveEventAsync(admin, onSale, hallId, "Published");
        await SaveEventAsync(admin, cancelled, hallId, "Cancelled");
        var sessions = new Dictionary<int, int>();
        await factory.WithDbAsync(async db =>
        {
            foreach (var id in new[] { onSale, cancelled, draft })
                sessions[id] = await db.EventSessions.Where(s => s.EventId == id).Select(s => s.Id).SingleAsync();
        });
        var visitor = factory.CreateClient();

        // Giriş istemeden açılır, giriş sayfasına yönlendirmez.
        var home = await visitor.GetAsync("/");
        Assert.Equal("/", home.RequestMessage!.RequestUri!.AbsolutePath);
        var html = await home.Content.ReadAsStringAsync();

        Assert.Contains(TitleOf(onSale), html);
        Assert.Contains($"href=\"/bilet/{sessions[onSale]}\"", html);
        Assert.DoesNotContain($"href=\"/bilet/{sessions[cancelled]}\"", html);
        Assert.Contains(TitleOf(onSale), await visitor.GetStringAsync($"/bilet/{sessions[onSale]}"));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await visitor.GetAsync($"/bilet/{sessions[draft]}")).StatusCode);
    }

    [MySqlFact]
    public async Task Gorsel_olmayan_dosya_afis_olarak_kabul_edilmez_gorsel_kabul_edilir()
    {
        var admin = await factory.SignInStaffAsync();
        var hallId = await CreateHallAsync(planVersions: 1);
        var eventId = await CreateEventAsync(admin, hallId);

        var rejected = await PostPosterAsync(admin, eventId, hallId, "afis.png", "<script>alert(1)</script>"u8.ToArray());
        Assert.Contains("JPEG, PNG ya da WebP görseli olmalı", rejected);

        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        await PostPosterAsync(admin, eventId, hallId, "afis.exe", png);
        await factory.WithDbAsync(async db =>
        {
            var fileName = (await db.Events.SingleAsync(e => e.Id == eventId)).PosterFileName;
            Assert.NotNull(fileName);
            Assert.EndsWith(".png", fileName);   // ad ve uzantı sunucuda, içeriğe göre verilir
        });
    }

    [MySqlFact]
    public async Task Etkinlik_yetkisi_olmayan_kullanici_etkinliklere_giremez()
    {
        var admin = await factory.SignInStaffAsync();
        var email = $"satis-{Guid.NewGuid():N}@test.local";
        await Forms.PostAsync(admin, "/yonetim/kullanicilar/duzenle",
        [
            new("Input.FullName", "Satış Personeli"), new("Input.Email", email), new("Input.Password", "Satis.12345"),
            new("Input.RoleIds", Role.SalesManagerId.ToString()), new("Input.IsActive", "true"),
        ]);
        var sales = await factory.SignInStaffAsync(email, "Satis.12345");

        var response = await sales.GetAsync("/yonetim/etkinlikler");

        Assert.Contains("/yonetim/yetkisiz", response.RequestMessage!.RequestUri!.ToString());
    }

    [MySqlFact]
    public async Task Yeni_etkinlik_birden_cok_seansla_tek_kayitta_olusturulur_bos_satir_yok_sayilir()
    {
        var admin = await factory.SignInStaffAsync();
        var hallId = await CreateHallAsync(planVersions: 1);
        var title = $"Etkinlik {Guid.NewGuid():N}"[..24];

        await Forms.PostAsync(admin, "/yonetim/etkinlikler/duzenle",
        [
            .. EventFields(title, hallId, "Published"),
            .. SessionFields("0", "2030-07-01", "20:00"), .. SessionFields("n3", "2030-07-02", "15:00"),
            new("SessionRows.Index", "n4"), new("SessionRows[n4].Date", ""), new("SessionRows[n4].Start", ""),
        ]);

        await factory.WithDbAsync(async db =>
        {
            var ev = await db.Events.Include(e => e.Sessions).SingleAsync(e => e.Title == title);
            Assert.Equal(EventStatus.Published, ev.Status);
            Assert.Equal([new DateTime(2030, 7, 1, 17, 0, 0), new DateTime(2030, 7, 2, 12, 0, 0)], ev.Sessions.Select(s => s.StartsAtUtc).Order());
        });
    }

    [MySqlFact]
    public async Task Kayitli_seansin_saati_degistirilir_kaldirilan_seans_silinir()
    {
        var admin = await factory.SignInStaffAsync();
        var hallId = await CreateHallAsync(planVersions: 1);
        var eventId = await CreateEventAsync(admin, hallId);
        await AddSessionAsync(admin, eventId, hallId, "2030-05-20", "20:00");
        await AddSessionAsync(admin, eventId, hallId, "2030-05-21", "20:00");
        int[] ids = [];
        await factory.WithDbAsync(async db => ids = await db.EventSessions.Where(s => s.EventId == eventId).OrderBy(s => s.StartsAtUtc).Select(s => s.Id).ToArrayAsync());

        await Forms.PostAsync(admin, $"/yonetim/etkinlikler/duzenle/{eventId}",
            [.. EventFields(TitleOf(eventId), hallId, "Draft"), .. SessionFields("0", "2030-05-20", "21:30", ids[0]), new("RemovedSessionIds", ids[1].ToString())]);

        await factory.WithDbAsync(async db =>
        {
            var session = Assert.Single(await db.EventSessions.Where(s => s.EventId == eventId).ToListAsync());
            Assert.Equal(ids[0], session.Id);
            Assert.Equal(new DateTime(2030, 5, 20, 18, 30, 0), session.StartsAtUtc);
        });
    }

    // ---- Yardımcılar ----

    private readonly Dictionary<int, string> _titles = [];

    private string TitleOf(int eventId) => _titles[eventId];

    /// <summary>Doğrudan veritabanına salon ve istenen sayıda kroki sürümü ekler.</summary>
    private async Task<int> CreateHallAsync(int planVersions)
    {
        var id = 0;
        await factory.WithDbAsync(async db =>
        {
            var adminId = await db.StaffUsers.Where(u => u.Email == MySqlWebAppFactory.AdminEmail).Select(u => u.Id).SingleAsync();
            var hall = new Hall { OrganizationId = Organization.DefaultId, Name = $"Salon {Guid.NewGuid():N}"[..20] };
            for (var v = 1; v <= planVersions; v++)
                hall.PlanVersions.Add(new HallPlanVersion { Version = v, PlanJson = HallPlanTests.SamplePlan, Capacity = 3, CreatedByStaffUserId = adminId });
            db.Halls.Add(hall);
            await db.SaveChangesAsync();
            id = hall.Id;
        });
        return id;
    }

    private async Task<int> CreateEventAsync(HttpClient admin, int hallId)
    {
        var title = $"Etkinlik {Guid.NewGuid():N}"[..24];
        await Forms.PostAsync(admin, "/yonetim/etkinlikler/duzenle", EventFields(title, hallId, "Draft"));
        var id = 0;
        await factory.WithDbAsync(async db => id = await db.Events.Where(e => e.Title == title).Select(e => e.Id).SingleAsync());
        _titles[id] = title;
        return id;
    }

    private Task<string> SaveEventAsync(HttpClient admin, int eventId, int hallId, string status) =>
        Forms.PostAsync(admin, $"/yonetim/etkinlikler/duzenle/{eventId}", EventFields(TitleOf(eventId), hallId, status));

    /// <summary>Etkinlik formunu tek yeni seans satırıyla kaydeder (kayıtlı seanslar formda gönderilmezse olduğu gibi kalır).</summary>
    private Task<string> AddSessionAsync(HttpClient admin, int eventId, int hallId, string date, string start) =>
        Forms.PostAsync(admin, $"/yonetim/etkinlikler/duzenle/{eventId}",
            [.. EventFields(TitleOf(eventId), hallId, "Draft"), .. SessionFields("n0", date, start)]);

    private static List<KeyValuePair<string, string>> SessionFields(string key, string date, string start, int? id = null) =>
    [
        new("SessionRows.Index", key), new($"SessionRows[{key}].Date", date), new($"SessionRows[{key}].Start", start),
        .. id is null ? [] : new KeyValuePair<string, string>[] { new($"SessionRows[{key}].Id", id.Value.ToString()) },
    ];

    private static List<KeyValuePair<string, string>> EventFields(string title, int hallId, string status) =>
    [
        new("Input.Title", title), new("Input.EventTypeId", EventType.ConcertId.ToString()), new("Input.HallId", hallId.ToString()),
        new("Input.Status", status), new("Input.SaleMode", "Sale"), new("Input.OnlineEnabled", "true"),
    ];

    /// <summary>Etkinlik formunu afiş dosyasıyla birlikte (multipart) gönderir.</summary>
    private async Task<string> PostPosterAsync(HttpClient admin, int eventId, int hallId, string fileName, byte[] bytes)
    {
        var url = $"/yonetim/etkinlikler/duzenle/{eventId}";
        var page = await admin.GetStringAsync(url);
        using var content = new MultipartFormDataContent();
        foreach (var (key, value) in EventFields(TitleOf(eventId), hallId, "Draft"))
            content.Add(new StringContent(value), key);
        content.Add(new StringContent(TokenPattern().Match(page).Groups[1].Value), "__RequestVerificationToken");
        content.Add(new ByteArrayContent(bytes), "Input.Poster", fileName);
        var response = await admin.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
