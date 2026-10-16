using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConferenceRoom.Domain.Sales;

/// <summary>
/// Koltuk kapsamı verisinin (<see cref="SeatScope.ScopeJson"/>) sunucu tarafındaki denetimi ve okunması.
/// Ekran tarayıcıda çalıştığı için gelen veriye güvenilmez: yapı, personel ve koltuk kimlikleri burada kontrol edilir,
/// geçerliyse sıralanmış, boşluksuz hâli döner.
///
/// Yapı:
///   { "schema": 1,
///     "staff": { "&lt;personel id&gt;": ["&lt;koltuk id&gt;", ...] }   personelin satabileceği koltuklar
///   }
/// Aynı koltuk birden çok personele verilebilir. Koltuğu boş liste olan personel kayıtta tutulmaz.
/// </summary>
public static class SeatScopeData
{
    public const int SchemaVersion = 1;

    /// <summary>Bir kayıttaki en fazla personel sayısı.</summary>
    public const int MaxStaff = 500;

    /// <summary>Kabul edilen en büyük veri (bayt).</summary>
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>Henüz hiç kapsam verilmemiş salon ya da etkinlik için boş veri.</summary>
    public static string Empty => new JsonObject { ["schema"] = SchemaVersion, ["staff"] = new JsonObject() }.ToJsonString();

    /// <summary>
    /// Veriyi denetler. <paramref name="seatIds"/>: salonun kroki sürümlerindeki koltuk kimlikleri;
    /// <paramref name="staffIds"/>: kurumun yönetim kullanıcılarının kimlikleri.
    /// </summary>
    public static SeatScopeResult Read(string json, IReadOnlySet<string> seatIds, IReadOnlySet<int> staffIds)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes)
            return SeatScopeResult.Fail("seatScope.tooLarge");

        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 6 }) as JsonObject
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return SeatScopeResult.Fail("seatScope.invalid");
        }
        if (root["schema"] is not JsonValue schema || schema.GetValueKind() != JsonValueKind.Number || schema.GetValue<double>() != SchemaVersion
            || root["staff"] is not JsonObject staffNodes || staffNodes.Count > MaxStaff)
            return SeatScopeResult.Fail("seatScope.invalid");

        // ---- Personel: kurumun kullanıcısı; koltuklar: krokide var, tekrarsız ----
        var staff = new SortedDictionary<int, List<string>>();
        foreach (var (key, value) in staffNodes)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var staffId) || key != staffId.ToString(CultureInfo.InvariantCulture)
                || value is not JsonArray seatNodes)
                return SeatScopeResult.Fail("seatScope.invalid");
            if (!staffIds.Contains(staffId))
                return SeatScopeResult.Fail("seatScope.unknownStaff");
            var seats = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var node in seatNodes)
            {
                if (node is not JsonValue v || !v.TryGetValue<string>(out var seatId) || !seats.Add(seatId))
                    return SeatScopeResult.Fail("seatScope.invalid");
                if (!seatIds.Contains(seatId))
                    return SeatScopeResult.Fail("seatScope.unknownSeat");
            }
            if (seats.Count > 0)
                staff[staffId] = [.. seats];
        }

        var normalized = new JsonObject { ["schema"] = SchemaVersion, ["staff"] = ToJson(staff) };
        return new SeatScopeResult(null, normalized.ToJsonString(), staff.Count);
    }

    /// <summary>Kaydedilmiş (denetlenmiş) veride bir personelin satabileceği koltuklar; personel kayıtta yoksa boş.</summary>
    public static IReadOnlySet<string> SeatsOf(string scopeJson, int staffUserId)
    {
        using var document = JsonDocument.Parse(scopeJson);
        var key = staffUserId.ToString(CultureInfo.InvariantCulture);
        return document.RootElement.GetProperty("staff").TryGetProperty(key, out var seats)
            ? seats.EnumerateArray().Select(s => s.GetString()!).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>Kaydedilmiş veride koltuk verilmiş personel sayısı (listelerde gösterilir).</summary>
    public static int StaffCount(string scopeJson)
    {
        using var document = JsonDocument.Parse(scopeJson);
        return document.RootElement.GetProperty("staff").EnumerateObject().Count();
    }

    private static JsonObject ToJson(SortedDictionary<int, List<string>> staff)
    {
        var result = new JsonObject();
        foreach (var (id, seats) in staff)
            result[id.ToString(CultureInfo.InvariantCulture)] = new JsonArray([.. seats.Select(s => (JsonNode)s)]);
        return result;
    }
}

/// <summary>Denetimin sonucu: hata varsa dil anahtarı, yoksa kaydedilecek JSON ve koltuk verilmiş personel sayısı.</summary>
public sealed record SeatScopeResult(string? ErrorKey, string? Json, int StaffCount)
{
    public bool IsValid => ErrorKey is null;

    internal static SeatScopeResult Fail(string errorKey) => new(errorKey, null, 0);
}
