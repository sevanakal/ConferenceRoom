using Microsoft.AspNetCore.SignalR;

namespace ConferenceRoom.Web.Sales;

/// <summary>
/// Süresi dolan koltuk ayırmalarını (<see cref="SeatHolds"/>) birkaç saniyede bir kaldırır ve seansın satış ekranlarına
/// "released" olarak bildirir. Ayıran ekran kendi süresini kendisi de sayar; bu yalnızca diğer ekranların haberdar olması içindir.
/// </summary>
public sealed class SeatHoldExpiry(SeatHolds holds, IHubContext<SalesHub> hub) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var (sessionId, seats) in holds.Expire())
                await hub.Clients.Group(SalesHub.Group(sessionId)).SendAsync("released", seats, stoppingToken);
        }
    }
}
