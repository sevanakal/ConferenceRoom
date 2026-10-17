namespace ConferenceRoom.Web.Sales;

/// <summary>
/// Koltuk ayırma: bir koltuğu seçen kişiye işlemi bitirmesi için <see cref="Duration"/> (3 dakika) verilir; o sürede başkası
/// o koltuğu seçemez ve satamaz, süre dolunca koltuk serbest kalır. Online ya da personel fark etmez.
///
/// Ayırmalar sunucunun belleğinde tutulur (tek sunucu; uygulama yeniden başlarsa ayırmalar düşer, satışlar veritabanındadır).
/// Ayıran, satış ekranının canlı bağlantısıdır (<see cref="SalesHub"/>, SignalR bağlantı kimliği): aynı kişinin iki sekmesi
/// birbirinden ayrıdır. Bağlantı kopunca ayırmaları kalkar. Süresi dolanları <see cref="SeatHoldExpiry"/> temizler ve
/// ekranlara bildirir. Tüm işlemler tek kilitle yapılır; koltuk sayıları küçük olduğu için yeterlidir.
/// </summary>
public sealed class SeatHolds(TimeProvider clock)
{
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(3);

    /// <summary>Bir koltuğun ayırması: kim (bağlantı ve personel), ne zamana kadar.</summary>
    private sealed record Hold(string Holder, int UserId, DateTime ExpiresAtUtc);

    private readonly Lock _gate = new();
    private readonly Dictionary<int, Dictionary<string, Hold>> _sessions = [];

    /// <summary>Ayırma sonucu: ayrılan koltuklar (koltuk → ayırmanın bittiği an, UTC) ve başkasında olduğu için ayrılamayanlar.</summary>
    public sealed record HoldResult(IReadOnlyDictionary<string, DateTime> Held, IReadOnlyList<string> Rejected);

    /// <summary>
    /// Koltukları bu bağlantı adına ayırır. Zaten bu bağlantıda olan koltuğun süresi değişmez (seçildiği andan sayılır).
    /// Bir bağlantı bir seansta en çok <see cref="SalesDesk.MaxSeatsPerOrder"/> koltuk ayırabilir; fazlası reddedilir.
    /// </summary>
    public HoldResult TryHold(int sessionId, string holder, int userId, IEnumerable<string> seatIds)
    {
        var now = Now;
        var expires = now + Duration;
        Dictionary<string, DateTime> held = new(StringComparer.Ordinal);
        List<string> rejected = [];
        lock (_gate)
        {
            var holds = SessionHolds(sessionId);
            var mine = holds.Values.Count(h => h.Holder == holder && h.ExpiresAtUtc > now);
            foreach (var seatId in seatIds.Distinct(StringComparer.Ordinal))
            {
                if (holds.TryGetValue(seatId, out var hold) && hold.ExpiresAtUtc > now)
                {
                    if (hold.Holder == holder)
                        held[seatId] = hold.ExpiresAtUtc;
                    else
                        rejected.Add(seatId);
                    continue;
                }
                if (mine >= SalesDesk.MaxSeatsPerOrder)
                {
                    rejected.Add(seatId);
                    continue;
                }
                holds[seatId] = new Hold(holder, userId, expires);
                held[seatId] = expires;
                mine++;
            }
        }
        return new HoldResult(held, rejected);
    }

    /// <summary>Bu bağlantının verilen koltuklardaki ayırmasını kaldırır; kaldırılanları döner.</summary>
    public IReadOnlyList<string> Release(int sessionId, string holder, IEnumerable<string> seatIds)
    {
        List<string> released = [];
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var holds))
                return released;
            foreach (var seatId in seatIds.Distinct(StringComparer.Ordinal))
            {
                if (holds.TryGetValue(seatId, out var hold) && hold.Holder == holder)
                {
                    holds.Remove(seatId);
                    released.Add(seatId);
                }
            }
        }
        return released;
    }

    /// <summary>Bağlantının tüm ayırmalarını kaldırır (bağlantı koptu); seans → kaldırılan koltuklar.</summary>
    public IReadOnlyDictionary<int, List<string>> ReleaseHolder(string holder) =>
        RemoveWhere((_, hold) => hold.Holder == holder);

    /// <summary>Süresi dolan ayırmaları kaldırır; seans → serbest kalan koltuklar.</summary>
    public IReadOnlyDictionary<int, List<string>> Expire()
    {
        var now = Now;
        return RemoveWhere((_, hold) => hold.ExpiresAtUtc <= now);
    }

    /// <summary>Seansta başkalarının ayırdığı koltuklar (ekranda "işlemde" görünür).</summary>
    public IReadOnlyList<string> HeldByOthers(int sessionId, string? holder)
    {
        var now = Now;
        lock (_gate)
        {
            return _sessions.TryGetValue(sessionId, out var holds)
                ? holds.Where(h => h.Value.Holder != holder && h.Value.ExpiresAtUtc > now).Select(h => h.Key).ToList()
                : [];
        }
    }

    /// <summary>
    /// Satış öncesi: koltuklardan başkasının ayırmasında olanlar. Ayırma yalnızca hem bağlantı (<paramref name="holder"/>)
    /// hem personel aynıysa satıcının kendisinin sayılır; başkasının bağlantı kimliğini göndermek ayırmayı aşmaz.
    /// </summary>
    public IReadOnlyList<string> BlockedFor(int sessionId, string? holder, int userId, IEnumerable<string> seatIds)
    {
        var now = Now;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var holds))
                return [];
            return seatIds.Where(id => holds.TryGetValue(id, out var hold) && hold.ExpiresAtUtc > now
                && !(hold.Holder == holder && hold.UserId == userId)).ToList();
        }
    }

    /// <summary>Satılan koltukların ayırmasını, kimde olursa olsun, kaldırır.</summary>
    public void Clear(int sessionId, IEnumerable<string> seatIds)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionId, out var holds))
            {
                foreach (var seatId in seatIds)
                    holds.Remove(seatId);
            }
        }
    }

    // ---- Yardımcılar ----

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private Dictionary<string, Hold> SessionHolds(int sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var holds))
            _sessions[sessionId] = holds = new Dictionary<string, Hold>(StringComparer.Ordinal);
        return holds;
    }

    private Dictionary<int, List<string>> RemoveWhere(Func<string, Hold, bool> match)
    {
        var removed = new Dictionary<int, List<string>>();
        lock (_gate)
        {
            foreach (var (sessionId, holds) in _sessions)
            {
                var seats = holds.Where(h => match(h.Key, h.Value)).Select(h => h.Key).ToList();
                foreach (var seatId in seats)
                    holds.Remove(seatId);
                if (seats.Count > 0)
                    removed[sessionId] = seats;
            }
            foreach (var empty in _sessions.Where(s => s.Value.Count == 0).Select(s => s.Key).ToList())
                _sessions.Remove(empty);
        }
        return removed;
    }
}
