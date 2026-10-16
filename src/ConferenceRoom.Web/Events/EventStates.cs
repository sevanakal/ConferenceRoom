using ConferenceRoom.Domain.Events;

namespace ConferenceRoom.Web.Events;

/// <summary>Listede ve panoda gösterilen durum: saklanan <see cref="EventStatus"/> + tüm seansları geçmiş yayındaki etkinlik için "Tamamlandı".</summary>
public enum EventState
{
    Draft,
    Published,
    SalesClosed,
    Cancelled,
    Completed,
}

public static class EventStates
{
    /// <param name="lastEndUtc">Son seansın bitişi (bitiş yazılmadıysa başlangıcı); seans yoksa null.</param>
    public static EventState Of(EventStatus status, DateTime? lastEndUtc, DateTime nowUtc) => status switch
    {
        EventStatus.Draft => EventState.Draft,
        EventStatus.Cancelled => EventState.Cancelled,
        _ when lastEndUtc is not null && lastEndUtc < nowUtc => EventState.Completed,
        EventStatus.SalesClosed => EventState.SalesClosed,
        _ => EventState.Published,
    };
}
