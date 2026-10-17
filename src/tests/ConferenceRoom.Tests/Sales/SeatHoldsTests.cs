using ConferenceRoom.Web.Sales;

namespace ConferenceRoom.Tests.Sales;

/// <summary>3 dakikalık koltuk ayırma (Web/Sales/SeatHolds.cs): kimin ayırdığı, süre, bırakma ve satış öncesi denetim.</summary>
public class SeatHoldsTests
{
    private readonly ManualClock _clock = new();
    private readonly SeatHolds _holds;

    public SeatHoldsTests() => _holds = new SeatHolds(_clock);

    [Fact]
    public void Ayrilan_koltugu_baska_ekran_ayiramaz_ve_isleme_ekranda_gorur()
    {
        var first = _holds.TryHold(1, "ekran-a", 10, ["s1", "s2"]);
        var second = _holds.TryHold(1, "ekran-b", 20, ["s2", "s3"]);

        Assert.Equal(["s1", "s2"], first.Held.Keys.Order());
        Assert.Equal(["s3"], second.Held.Keys);
        Assert.Equal(["s2"], second.Rejected);
        Assert.Equal(["s1", "s2"], _holds.HeldByOthers(1, "ekran-b").Order());
        Assert.Empty(_holds.TryHold(2, "ekran-b", 20, ["s1"]).Rejected);   // başka seans ayrıdır
    }

    [Fact]
    public void Uc_dakika_dolunca_koltuk_serbest_kalir_sure_yeniden_secmekle_uzamaz()
    {
        var expires = _holds.TryHold(1, "ekran-a", 10, ["s1"]).Held["s1"];
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(expires, _holds.TryHold(1, "ekran-a", 10, ["s1"]).Held["s1"]);

        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(["s1"], _holds.Expire()[1]);
        Assert.Empty(_holds.TryHold(1, "ekran-b", 20, ["s1"]).Rejected);
    }

    [Fact]
    public void Birakilan_ve_baglantisi_kopan_ekranin_ayirmalari_kalkar()
    {
        _holds.TryHold(1, "ekran-a", 10, ["s1", "s2"]);
        _holds.TryHold(2, "ekran-a", 10, ["s9"]);

        Assert.Empty(_holds.Release(1, "ekran-b", ["s1"]));   // başkasının ayırması bırakılamaz
        Assert.Equal(["s1"], _holds.Release(1, "ekran-a", ["s1"]));
        var released = _holds.ReleaseHolder("ekran-a");

        Assert.Equal(["s2"], released[1]);
        Assert.Equal(["s9"], released[2]);
        Assert.Empty(_holds.HeldByOthers(1, null));
    }

    [Fact]
    public void Satista_koltuk_yalnizca_ayni_ekran_ve_ayni_personel_icin_serbesttir()
    {
        _holds.TryHold(1, "ekran-a", 10, ["s1"]);

        Assert.Empty(_holds.BlockedFor(1, "ekran-a", 10, ["s1", "s2"]));
        Assert.Equal(["s1"], _holds.BlockedFor(1, "ekran-b", 20, ["s1"]));
        Assert.Equal(["s1"], _holds.BlockedFor(1, "ekran-a", 20, ["s1"]));   // başkasının bağlantı kimliğini göndermek yetmez
        Assert.Equal(["s1"], _holds.BlockedFor(1, null, 10, ["s1"]));
    }

    [Fact]
    public void Bir_ekran_bir_seansta_en_fazla_islem_siniri_kadar_koltuk_ayirabilir()
    {
        var seats = Enumerable.Range(1, SalesDeskLimit + 5).Select(i => $"s{i}").ToList();

        var result = _holds.TryHold(1, "ekran-a", 10, seats);

        Assert.Equal(SalesDeskLimit, result.Held.Count);
        Assert.Equal(5, result.Rejected.Count);
    }

    private const int SalesDeskLimit = SalesDesk.MaxSeatsPerOrder;

    /// <summary>Elle ilerletilen saat.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
