namespace ConferenceRoom.Tests.Database;

/// <summary>
/// Gerçek MySQL'e bağlanan test sınıflarını aynı koleksiyona toplar ve sırayla çalıştırır.
/// Neden: Oracle'ın MySql.Data sürücüsü SSL bağlantısını kurarken paylaşılan bir sözlüğe kilitsiz yazıyor;
/// TLS açık sunucuda (CI'daki MySQL 8.4) aynı anda açılan bağlantılar "non-concurrent collections" hatası veriyor.
/// Yeni bir MySQL test sınıfı eklenirken üstüne <c>[Collection(MySqlCollection.Name)]</c> yazılır.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class MySqlCollection
{
    public const string Name = "MySql";
}
