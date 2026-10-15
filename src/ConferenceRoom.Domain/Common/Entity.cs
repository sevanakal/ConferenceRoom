namespace ConferenceRoom.Domain.Common;

/// <summary>
/// Tüm varlıkların ortak tabanı. Her tablo otomatik artan bir <see cref="Id"/>
/// ve kaydın oluşturulma zamanını (UTC) taşır.
/// </summary>
public abstract class Entity
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
