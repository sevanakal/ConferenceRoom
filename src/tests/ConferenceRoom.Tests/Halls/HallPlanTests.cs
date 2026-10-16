using System.Text.Json.Nodes;
using ConferenceRoom.Domain.Halls;

namespace ConferenceRoom.Tests.Halls;

/// <summary>Kroki verisinin sunucu tarafı denetimi (veritabanı gerekmez).</summary>
public class HallPlanTests
{
    /// <summary>Bir katta bir blok, iki normal koltuk, bir ekstra sandalye ve bir sahne.</summary>
    internal const string SamplePlan = """
        { "schema": 1, "unit": "cm", "name": "eski ad",
          "levels": [{ "id": "p", "name": "Parter" }],
          "blocks": [{ "id": "b1", "levelId": "p", "name": "Orta", "color": "#1D5FBF" }],
          "items": [
            { "id": "s1", "levelId": "p", "type": "seat", "x": 100, "y": 200, "rotation": 0, "row": "A", "num": 1, "kind": "normal", "blockId": "b1" },
            { "id": "s2", "levelId": "p", "type": "seat", "x": 160, "y": 200, "rotation": 0, "row": "A", "num": 2, "kind": "vip", "blockId": "b1" },
            { "id": "s3", "levelId": "p", "type": "seat", "x": 220, "y": 200, "rotation": 0, "row": "EK", "num": 1, "kind": "extra", "blockId": null },
            { "id": "st", "levelId": "p", "type": "stage", "x": 160, "y": 50, "w": 400, "h": 100, "rotation": 0, "label": "SAHNE" }
          ] }
        """;

    [Fact]
    public void Gecerli_kroki_kabul_edilir_ve_kapasite_ekstra_sandalyeyle_sayilir()
    {
        var result = HallPlan.Read(SamplePlan, "Ana Salon");

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Capacity);
    }

    [Fact]
    public void Kaydedilen_krokinin_adi_salon_adindan_gelir()
    {
        var result = HallPlan.Read(SamplePlan, "Ana Salon");

        Assert.Equal("Ana Salon", JsonNode.Parse(result.Json!)!["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("bu json değil")]
    [InlineData("[]")]
    [InlineData("""{ "schema": 2, "unit": "cm", "levels": [{ "id": "p", "name": "P" }], "blocks": [], "items": [] }""")]
    [InlineData("""{ "schema": 1, "unit": "cm", "levels": [], "blocks": [], "items": [] }""")]
    [InlineData("""{ "schema": 1, "unit": "cm", "levels": [{ "id": "p", "name": "P" }], "blocks": [], "items": [{ "id": "x", "levelId": "yok", "type": "seat", "x": 0, "y": 0 }] }""")]
    [InlineData("""{ "schema": 1, "unit": "cm", "levels": [{ "id": "p", "name": "P" }], "blocks": [], "items": [{ "id": "x", "levelId": "p", "type": "uzay gemisi", "x": 0, "y": 0 }] }""")]
    [InlineData("""{ "schema": 1, "unit": "cm", "levels": [{ "id": "p", "name": "P" }], "blocks": [], "items": [{ "id": "x", "levelId": "p", "type": "seat", "x": 0, "y": 0, "blockId": "yok" }] }""")]
    [InlineData("""{ "schema": 1, "unit": "cm", "levels": [{ "id": "p", "name": "P" }], "blocks": [], "items": [{ "id": "x", "levelId": "p", "type": "seat", "x": 0, "y": 0 }, { "id": "x", "levelId": "p", "type": "seat", "x": 9, "y": 0 }] }""")]
    public void Bozuk_kroki_reddedilir(string json)
    {
        var result = HallPlan.Read(json, "Ana Salon");

        Assert.False(result.IsValid);
        Assert.Equal("hallPlan.invalid", result.ErrorKey);
    }

    [Fact]
    public void Bos_kroki_gecerli_bir_krokidir()
    {
        var result = HallPlan.Read(HallPlan.Blank("Fuaye", "Parter"), "Fuaye");

        Assert.True(result.IsValid);
        Assert.Equal(0, result.Capacity);
    }
}
