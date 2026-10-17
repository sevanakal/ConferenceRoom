using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ConferenceRoom.Domain.Events;

/// <summary>
/// Etkinlik koltuk ayarlarının (<see cref="EventSeatMap.MapJson"/>) sunucu tarafındaki denetimi.
/// Ekran tarayıcıda çalıştığı için gelen veriye güvenilmez: yapı, kimlikler, fiyatlar ve kurallar burada kontrol edilir,
/// geçerliyse boşluksuz (sıkıştırılmış) hâli ve özet sayılar döner.
///
/// Yapı:
///   { "schema": 1,
///     "categories": [{ "id", "name", "price", "color", "protocol" }],   bilet kategorileri; price TL, protocol: satışa kapalı
///     "seats":  { "&lt;koltuk id&gt;": "&lt;kategori id&gt;" },                    kategorisi olmayan koltuk satılmaz
///     "online": ["&lt;koltuk id&gt;", ...],                                  internetten satılabilecek koltuklar
///     "showOnlinePrice": true                                           online satışta fiyat gösterilsin mi (yoksa true)
///   }
/// Kurallar: koltuk kimlikleri etkinliğin kroki sürümlerinde bulunmalı; online koltuğun protokol olmayan bir kategorisi olmalı.
/// </summary>
public static partial class SeatMap
{
    public const int SchemaVersion = 1;

    public const int MaxCategories = 50;

    public const int MaxNameLength = 60;

    public const decimal MaxPrice = 1_000_000m;

    /// <summary>Kabul edilen en büyük veri (bayt). 50.000 koltuğun hepsi atanmış ve online olsa yaklaşık 2 MB tutar.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>Henüz hiç ayar yapılmamış etkinlik için boş ayarlar.</summary>
    public static string Empty => new JsonObject
    {
        ["schema"] = SchemaVersion,
        ["categories"] = new JsonArray(),
        ["seats"] = new JsonObject(),
        ["online"] = new JsonArray(),
        ["showOnlinePrice"] = true,
    }.ToJsonString();

    /// <summary>Ayarları denetler. <paramref name="seatIds"/>: etkinliğin seanslarının bağlı olduğu kroki sürümlerindeki koltuk kimlikleri.</summary>
    public static SeatMapResult Read(string json, IReadOnlySet<string> seatIds)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes)
            return SeatMapResult.Fail("seatMap.tooLarge");

        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 8 }) as JsonObject
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return SeatMapResult.Fail("seatMap.invalid");
        }
        if (Number(root["schema"]) != SchemaVersion)
            return SeatMapResult.Fail("seatMap.invalid");

        // ---- Kategoriler: benzersiz kimlik, dolu ad, 0 – 1.000.000 TL fiyat, #RRGGBB renk ----
        if (root["categories"] is not JsonArray categoryNodes || categoryNodes.Count > MaxCategories)
            return SeatMapResult.Fail("seatMap.invalid");
        var categories = new JsonArray();
        var protocolIds = new HashSet<string>(StringComparer.Ordinal);
        var categoryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in categoryNodes)
        {
            if (node is not JsonObject c || Text(c["id"]) is not { } id || !IdPattern().IsMatch(id) || !categoryIds.Add(id))
                return SeatMapResult.Fail("seatMap.invalid");
            var name = Text(c["name"])?.Trim() ?? "";
            if (name.Length is 0 or > MaxNameLength)
                return SeatMapResult.Fail("seatMap.categoryName");
            if (Number(c["price"]) is not { } price || price < 0 || price > (double)MaxPrice)
                return SeatMapResult.Fail("seatMap.categoryPrice");
            if (Text(c["color"]) is not { } color || !ColorPattern().IsMatch(color))
                return SeatMapResult.Fail("seatMap.invalid");
            var protocol = c["protocol"] is JsonValue p && p.GetValueKind() == JsonValueKind.True;
            if (protocol)
                protocolIds.Add(id);
            categories.Add(new JsonObject
            {
                ["id"] = id,
                ["name"] = name,
                ["price"] = Math.Round((decimal)price, 2, MidpointRounding.AwayFromZero),
                ["color"] = color.ToUpperInvariant(),
                ["protocol"] = protocol,
            });
        }

        // ---- Koltuk → kategori: koltuk krokide, kategori listede olmalı ----
        if (root["seats"] is not JsonObject seatNodes)
            return SeatMapResult.Fail("seatMap.invalid");
        var seats = new JsonObject();
        foreach (var (seatId, value) in seatNodes)
        {
            if (!seatIds.Contains(seatId))
                return SeatMapResult.Fail("seatMap.unknownSeat");
            if (Text(value) is not { } categoryId || !categoryIds.Contains(categoryId))
                return SeatMapResult.Fail("seatMap.invalid");
            seats[seatId] = categoryId;
        }

        // ---- Online koltuklar: kategorisi olan, protokol olmayan koltuklar ----
        if (root["online"] is not JsonArray onlineNodes)
            return SeatMapResult.Fail("seatMap.invalid");
        var online = new JsonArray();
        var onlineIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in onlineNodes)
        {
            if (Text(node) is not { } seatId || !onlineIds.Add(seatId))
                return SeatMapResult.Fail("seatMap.invalid");
            if (!seatIds.Contains(seatId))
                return SeatMapResult.Fail("seatMap.unknownSeat");
            if (Text(seats[seatId]) is not { } categoryId)
                return SeatMapResult.Fail("seatMap.onlineNeedsCategory");
            if (protocolIds.Contains(categoryId))
                return SeatMapResult.Fail("seatMap.onlineProtocol");
            online.Add(seatId);
        }

        // ---- Online satışta fiyat gösterimi: yalnızca açıkça false ise gizlenir ----
        var showOnlinePrice = !(root["showOnlinePrice"] is JsonValue show && show.GetValueKind() == JsonValueKind.False);

        var normalized = new JsonObject
        {
            ["schema"] = SchemaVersion,
            ["categories"] = categories,
            ["seats"] = seats,
            ["online"] = online,
            ["showOnlinePrice"] = showOnlinePrice,
        };
        return new SeatMapResult(null, normalized.ToJsonString(), seats.Count, online.Count);
    }

    /// <summary>
    /// Kaydedilmiş (denetlenmiş) ayarlardan satış için gerekenler: koltuk id → kategori. Satış her işlemde fiyatı
    /// ve satılabilirliği buradan alır; kategorisi olmayan ya da protokol kategorisindeki koltuk satılmaz.
    /// </summary>
    public static IReadOnlyDictionary<string, SeatCategory> SeatCategories(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var categories = new Dictionary<string, SeatCategory>(StringComparer.Ordinal);
        foreach (var node in root["categories"]!.AsArray())
        {
            var c = node!.AsObject();
            categories[Text(c["id"])!] = new SeatCategory(Text(c["id"])!, Text(c["name"])!, c["price"]!.GetValue<decimal>(), Text(c["color"])!,
                c["protocol"] is JsonValue p && p.GetValueKind() == JsonValueKind.True);
        }
        var seats = new Dictionary<string, SeatCategory>(StringComparer.Ordinal);
        foreach (var (seatId, value) in root["seats"]!.AsObject())
        {
            if (Text(value) is { } categoryId && categories.TryGetValue(categoryId, out var category))
                seats[seatId] = category;
        }
        return seats;
    }

    // ---- Yardımcılar: alan yoksa ya da türü farklıysa null ----

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && double.IsFinite(value.GetValue<double>()) ? value.GetValue<double>() : null;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,20}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}

/// <summary>Bilet kategorisi (fiyat TL; protokol: satışa kapalı).</summary>
public sealed record SeatCategory(string Id, string Name, decimal Price, string Color, bool Protocol);

/// <summary>Denetimin sonucu: hata varsa dil anahtarı, yoksa kaydedilecek JSON ve özet (kategorisi olan / online koltuk sayısı).</summary>
public sealed record SeatMapResult(string? ErrorKey, string? Json, int AssignedSeats, int OnlineSeats)
{
    public bool IsValid => ErrorKey is null;

    internal static SeatMapResult Fail(string errorKey) => new(errorKey, null, 0, 0);
}
