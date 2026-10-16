using ConferenceRoom.Domain.Events;
using ConferenceRoom.Web.Events;

namespace ConferenceRoom.Tests.Events;

/// <summary>Panoda ve listede gösterilen durumların hesaplanması (veritabanı gerekmez).</summary>
public class BoardBadgeTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tomorrow = Now.AddDays(1);

    [Theory]
    [InlineData(EventStatus.Published, SaleMode.Sale, true, BoardBadge.OnSale)]
    [InlineData(EventStatus.Published, SaleMode.Reservation, true, BoardBadge.ReservationOpen)]
    [InlineData(EventStatus.Published, SaleMode.Sale, false, BoardBadge.BoxOffice)]
    [InlineData(EventStatus.SalesClosed, SaleMode.Sale, true, BoardBadge.SalesClosed)]
    [InlineData(EventStatus.Cancelled, SaleMode.Sale, true, BoardBadge.Cancelled)]
    public void Yarinki_seansin_durumu_etkinligin_durumuna_ve_satis_turune_gore_belirlenir(EventStatus status, SaleMode mode, bool online, BoardBadge expected) =>
        Assert.Equal(expected, BoardQuery.BadgeOf(status, mode, online, null, null, Tomorrow, Now));

    [Fact]
    public void Satisi_henuz_acilmamis_seans_yakinda_gorunur() =>
        Assert.Equal(BoardBadge.SalesSoon, BoardQuery.BadgeOf(EventStatus.Published, SaleMode.Sale, true, Now.AddHours(3), null, Tomorrow, Now));

    [Fact]
    public void Satis_bitis_tarihi_gecmis_seans_satis_kapandi_gorunur() =>
        Assert.Equal(BoardBadge.SalesClosed, BoardQuery.BadgeOf(EventStatus.Published, SaleMode.Sale, true, null, Now.AddHours(-1), Tomorrow, Now));

    [Fact]
    public void Baslamis_seans_su_anda_gorunur_iptal_edilmisse_iptal_gorunur()
    {
        Assert.Equal(BoardBadge.Now, BoardQuery.BadgeOf(EventStatus.Published, SaleMode.Sale, true, null, null, Now.AddMinutes(-10), Now));
        Assert.Equal(BoardBadge.Cancelled, BoardQuery.BadgeOf(EventStatus.Cancelled, SaleMode.Sale, true, null, null, Now.AddMinutes(-10), Now));
    }

    [Fact]
    public void Tum_seanslari_gecmis_yayindaki_etkinlik_tamamlandi_sayilir_taslak_ve_iptal_oldugu_gibi_kalir()
    {
        Assert.Equal(EventState.Completed, EventStates.Of(EventStatus.Published, Now.AddDays(-1), Now));
        Assert.Equal(EventState.Published, EventStates.Of(EventStatus.Published, Tomorrow, Now));
        Assert.Equal(EventState.Draft, EventStates.Of(EventStatus.Draft, Now.AddDays(-1), Now));
        Assert.Equal(EventState.Cancelled, EventStates.Of(EventStatus.Cancelled, Now.AddDays(-1), Now));
    }
}
