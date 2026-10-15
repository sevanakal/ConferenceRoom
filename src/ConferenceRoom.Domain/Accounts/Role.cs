using ConferenceRoom.Domain.Common;

namespace ConferenceRoom.Domain.Accounts;

/// <summary>
/// Yönetim tarafındaki rol (Yönetici, Salon sorumlusu, Satış sorumlusu...). Roller kodda değil
/// veritabanında tanımlıdır; bir rolün ne yapabileceğini <see cref="Permissions"/> listesindeki
/// yetkilerden seçilenler belirler.
/// </summary>
public class Role : Entity, IOrganizationScoped
{
    /// <summary>Kurulumla gelen rollerin kimlikleri. İlk yönetici hesabı <see cref="AdministratorId"/> rolünü alır.</summary>
    public const int AdministratorId = 1, HallManagerId = 2, SalesManagerId = 3;

    public int OrganizationId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Kurulumla gelen rol. Arayüzden silinmesi engellenecek (Yönetici rolü kaybolmasın diye).</summary>
    public bool IsSystem { get; set; }

    public ICollection<RolePermission> Permissions { get; set; } = [];
}

/// <summary>Bir rolün sahip olduğu tek bir yetki (rol + yetki kodu).</summary>
public class RolePermission
{
    public int RoleId { get; set; }

    /// <summary><see cref="Accounts.Permissions"/> içindeki kodlardan biri.</summary>
    public required string Permission { get; set; }
}
