using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConferenceRoom.Domain.Halls;

/// <summary>
/// Kroki verisinin (editörün kaydettiği JSON) sunucu tarafındaki denetimi.
/// Editör tarayıcıda çalıştığı için gelen veriye güvenilmez: yapı, kimliklerin tutarlılığı ve boyut burada kontrol edilir,
/// geçerliyse salon adı işlenmiş, boşluksuz (sıkıştırılmış) hâli ve kapasite özeti döner.
///
/// Yapı (ölçüler cm, x/y nesnenin merkezi):
///   { "schema": 1, "unit": "cm", "name": "...",
///     "levels": [{ "id", "name" }],                         katlar (Parter, Balkon...)
///     "blocks": [{ "id", "levelId", "name", "color" }],     koltuk blokları, kata bağlı
///     "items":  [{ "id", "levelId", "type", "x", "y", ... }] çizimdeki her nesne
///   }
/// Ayrıntılı alan listesi: src/ConferenceRoom.Web/wwwroot/js/kroki-editor.js dosyasının "Belge" bölümü.
/// </summary>
public static class HallPlan
{
    /// <summary>Kabul edilen en büyük kroki verisi (bayt). 10.000 koltuklu bir salon yaklaşık 2 MB tutar.</summary>
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>Bir krokideki en fazla öğe (koltuk, duvar, yazı...) sayısı.</summary>
    public const int MaxItems = 50_000;

    public const int SchemaVersion = 1;

    private static readonly HashSet<string> ItemTypes = ["seat", "stage", "area", "wall", "door", "column", "text"];
    private static readonly HashSet<string> SeatKinds = ["normal", "vip", "accessible", "extra"];

    /// <summary>Kroki verisini denetler; <paramref name="hallName"/> verinin "name" alanına yazılır (salon adı tek yerden gelsin).</summary>
    public static HallPlanResult Read(string json, string hallName)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes)
            return HallPlanResult.Fail("hallPlan.tooLarge");

        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 16 }) as JsonObject
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return HallPlanResult.Fail("hallPlan.invalid");
        }

        // ---- Başlık alanları ----
        if (Number(root["schema"]) != SchemaVersion || Text(root["unit"]) != "cm")
            return HallPlanResult.Fail("hallPlan.invalid");

        // ---- Katlar: en az bir tane, kimlikler benzersiz ----
        if (root["levels"] is not JsonArray levels || levels.Count == 0)
            return HallPlanResult.Fail("hallPlan.invalid");
        var levelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var level in levels)
        {
            if (level is not JsonObject l || Text(l["id"]) is not { Length: > 0 } id || Text(l["name"]) is null || !levelIds.Add(id))
                return HallPlanResult.Fail("hallPlan.invalid");
        }

        // ---- Bloklar: var olan bir kata bağlı ----
        if (root["blocks"] is not JsonArray blocks)
            return HallPlanResult.Fail("hallPlan.invalid");
        var blockIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in blocks)
        {
            if (block is not JsonObject b || Text(b["id"]) is not { Length: > 0 } id || !levelIds.Contains(Text(b["levelId"]) ?? "") || !blockIds.Add(id))
                return HallPlanResult.Fail("hallPlan.invalid");
        }

        // ---- Öğeler: bilinen tür, var olan kat, sayısal konum; koltuklarda tür ve blok ----
        if (root["items"] is not JsonArray items)
            return HallPlanResult.Fail("hallPlan.invalid");
        if (items.Count > MaxItems)
            return HallPlanResult.Fail("hallPlan.tooLarge");
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        int fixedSeats = 0, extraChairs = 0;
        foreach (var item in items)
        {
            if (item is not JsonObject i
                || Text(i["id"]) is not { Length: > 0 } id || !itemIds.Add(id)
                || !levelIds.Contains(Text(i["levelId"]) ?? "")
                || Text(i["type"]) is not { } type || !ItemTypes.Contains(type)
                || Number(i["x"]) is null || Number(i["y"]) is null)
                return HallPlanResult.Fail("hallPlan.invalid");

            if (type != "seat")
                continue;
            var kind = Text(i["kind"]) ?? "normal";
            var blockId = i["blockId"];
            if (!SeatKinds.Contains(kind) || (blockId is not null && !blockIds.Contains(Text(blockId) ?? "")))
                return HallPlanResult.Fail("hallPlan.invalid");
            if (kind == "extra") extraChairs++;
            else fixedSeats++;
        }

        root["name"] = hallName;
        return new HallPlanResult(null, root.ToJsonString(), fixedSeats + extraChairs);
    }

    /// <summary>Kaydedilmiş (denetlenmiş) kroki verisindeki koltukların kimlikleri; etkinlik koltuk ayarları bunlara bağlanır.</summary>
    public static IEnumerable<string> SeatIds(string planJson)
    {
        using var document = JsonDocument.Parse(planJson);
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("type").GetString() == "seat")
                yield return item.GetProperty("id").GetString()!;
        }
    }

    /// <summary>Henüz krokisi olmayan salon için editörün açılacağı boş kroki (tek kat).</summary>
    public static string Blank(string hallName, string firstLevelName) => new JsonObject
    {
        ["schema"] = SchemaVersion,
        ["unit"] = "cm",
        ["name"] = hallName,
        ["levels"] = new JsonArray(new JsonObject { ["id"] = "parter", ["name"] = firstLevelName }),
        ["blocks"] = new JsonArray(),
        ["items"] = new JsonArray(),
    }.ToJsonString();

    // ---- Yardımcılar: alan yoksa ya da türü farklıysa null ----

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && double.IsFinite(value.GetValue<double>()) ? value.GetValue<double>() : null;
}

/// <summary>Denetimin sonucu: hata varsa dil anahtarı, yoksa kaydedilecek JSON ve toplam kapasite.</summary>
public sealed record HallPlanResult(string? ErrorKey, string? Json, int Capacity)
{
    public bool IsValid => ErrorKey is null;

    internal static HallPlanResult Fail(string errorKey) => new(errorKey, null, 0);
}
