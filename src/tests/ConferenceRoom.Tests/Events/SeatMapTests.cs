using System.Text.Json.Nodes;
using ConferenceRoom.Domain.Events;

namespace ConferenceRoom.Tests.Events;

/// <summary>Etkinlik koltuk ayarlarının sunucu denetimi (Domain/Events/SeatMap.cs): kategoriler, atamalar, online kuralları.</summary>
public class SeatMapTests
{
    private static readonly HashSet<string> Seats = ["s1", "s2", "s3"];

    private static string Map(string seats = """{ "s1": "c1", "s2": "c2" }""", string online = """["s1"]""", string price = "250.5") => $$"""
        { "schema": 1,
          "categories": [
            { "id": "c1", "name": " 1. Kategori ", "price": {{price}}, "color": "#1d5fbf", "protocol": false },
            { "id": "c2", "name": "Protokol", "price": 0, "color": "#C9A227", "protocol": true } ],
          "seats": {{seats}},
          "online": {{online}} }
        """;

    [Fact]
    public void Gecerli_ayarlar_kabul_edilir_ad_ve_renk_duzeltilir()
    {
        var result = SeatMap.Read(Map(), Seats);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.AssignedSeats);
        Assert.Equal(1, result.OnlineSeats);
        var category = JsonNode.Parse(result.Json!)!["categories"]![0]!;
        Assert.Equal("1. Kategori", category["name"]!.GetValue<string>());
        Assert.Equal("#1D5FBF", category["color"]!.GetValue<string>());
        Assert.Equal(250.5m, category["price"]!.GetValue<decimal>());
    }

    [Fact]
    public void Rezervasyon_kategorisinin_fiyati_sifir_saklanir_ve_online_olabilir()
    {
        var json = Map().Replace("\"price\": 250.5, \"color\": \"#1d5fbf\", \"protocol\": false", "\"price\": 250.5, \"color\": \"#1d5fbf\", \"protocol\": false, \"reservation\": true");

        var result = SeatMap.Read(json, Seats);

        Assert.True(result.IsValid);
        var categories = JsonNode.Parse(result.Json!)!["categories"]!;
        Assert.Equal(0m, categories[0]!["price"]!.GetValue<decimal>());
        Assert.True(categories[0]!["reservation"]!.GetValue<bool>());
        Assert.False(categories[1]!["reservation"]!.GetValue<bool>());   // alan yoksa rezervasyon değildir
        Assert.Equal(1, result.OnlineSeats);
    }

    [Fact]
    public void Krokide_olmayan_koltuk_reddedilir()
    {
        Assert.Equal("seatMap.unknownSeat", SeatMap.Read(Map(seats: """{ "yok": "c1" }""", online: "[]"), Seats).ErrorKey);
        Assert.Equal("seatMap.unknownSeat", SeatMap.Read(Map(online: """["yok"]"""), Seats).ErrorKey);
    }

    [Fact]
    public void Online_koltugun_protokol_olmayan_kategorisi_olmali()
    {
        Assert.Equal("seatMap.onlineNeedsCategory", SeatMap.Read(Map(online: """["s3"]"""), Seats).ErrorKey);
        Assert.Equal("seatMap.onlineProtocol", SeatMap.Read(Map(online: """["s2"]"""), Seats).ErrorKey);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1000001")]
    [InlineData("\"250\"")]
    public void Gecersiz_fiyat_reddedilir(string price)
    {
        Assert.Equal("seatMap.categoryPrice", SeatMap.Read(Map(price: price), Seats).ErrorKey);
    }

    [Theory]
    [InlineData("bu json değil")]
    [InlineData("""{ "schema": 2, "categories": [], "seats": {}, "online": [] }""")]
    [InlineData("""{ "schema": 1, "categories": [], "seats": { "s1": "olmayan" }, "online": [] }""")]
    [InlineData("""{ "schema": 1, "categories": [{ "id": "c1", "name": "A", "price": 1, "color": "kırmızı" }], "seats": {}, "online": [] }""")]
    public void Bozuk_ayarlar_reddedilir(string json)
    {
        Assert.Equal("seatMap.invalid", SeatMap.Read(json, Seats).ErrorKey);
    }

    [Fact]
    public void Bos_ayarlar_gecerlidir()
    {
        Assert.True(SeatMap.Read(SeatMap.Empty, Seats).IsValid);
    }
}
