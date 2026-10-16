using ConferenceRoom.Domain.Sales;

namespace ConferenceRoom.Tests.Sales;

/// <summary>Personelin satabileceği koltuklar verisinin sunucu denetimi (Domain/Sales/SeatScopeData.cs).</summary>
public class SeatScopeDataTests
{
    private static readonly HashSet<string> Seats = ["s1", "s2", "s3"];
    private static readonly HashSet<int> Staff = [5, 12];

    private static SeatScopeResult Read(string staff) => SeatScopeData.Read($$"""{ "schema": 1, "staff": {{staff}} }""", Seats, Staff);

    [Fact]
    public void Gecerli_kapsam_siralanir_ve_koltugu_olmayan_personel_atilir()
    {
        var result = Read("""{ "12": ["s3", "s1"], "5": [] }""");

        Assert.True(result.IsValid);
        Assert.Equal(1, result.StaffCount);
        Assert.Equal("""{"schema":1,"staff":{"12":["s1","s3"]}}""", result.Json);
    }

    [Fact]
    public void Krokide_olmayan_koltuk_ve_listede_olmayan_personel_reddedilir()
    {
        Assert.Equal("seatScope.unknownSeat", Read("""{ "5": ["yok"] }""").ErrorKey);
        Assert.Equal("seatScope.unknownStaff", Read("""{ "99": ["s1"] }""").ErrorKey);
    }

    [Theory]
    [InlineData("""{ "abc": ["s1"] }""")]
    [InlineData("""{ "05": ["s1"] }""")]
    [InlineData("""{ "5": ["s1", "s1"] }""")]
    [InlineData("""{ "5": [1] }""")]
    [InlineData("""{ "5": "s1" }""")]
    [InlineData("""[]""")]
    public void Bozuk_veri_reddedilir(string staff) => Assert.Equal("seatScope.invalid", Read(staff).ErrorKey);

    [Fact]
    public void Kayitli_veriden_personelin_koltuklari_okunur()
    {
        var json = Read("""{ "12": ["s1", "s2"] }""").Json!;

        Assert.Equal(["s1", "s2"], SeatScopeData.SeatsOf(json, 12).Order());
        Assert.Empty(SeatScopeData.SeatsOf(json, 5));
        Assert.Equal(1, SeatScopeData.StaffCount(json));
    }
}
