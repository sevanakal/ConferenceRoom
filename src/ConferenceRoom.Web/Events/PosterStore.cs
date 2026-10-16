namespace ConferenceRoom.Web.Events;

/// <summary>
/// Etkinlik afişlerini diskte saklar ve "/afisler/{dosya}" adresinden sunulmasını sağlar (Program.cs).
/// Klasör "Storage:PostersPath" ayarından gelir; yoksa uygulama klasöründe "data/posters".
/// Docker'da bu klasör ayrı bir birimdir (docker-compose.yml: posters), imaj yenilense de afişler kalır.
///
/// Gelen dosyaya uzantısına bakılarak güvenilmez: ilk baytları okunur, yalnızca JPEG, PNG ve WebP kabul edilir.
/// Dosya adı her zaman sunucuda üretilir (rastgele ad + gerçek tür uzantısı); kullanıcının verdiği ad hiç kullanılmaz.
/// </summary>
public sealed class PosterStore
{
    public const string RequestPath = "/afisler";

    /// <summary>Kabul edilen en büyük afiş (bayt).</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    public PosterStore(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["Storage:PostersPath"];
        Root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(environment.ContentRootPath, "data", "posters") : configured);
        Directory.CreateDirectory(Root);
    }

    /// <summary>Afişlerin durduğu klasörün tam yolu.</summary>
    public string Root { get; }

    public static string Url(string fileName) => $"{RequestPath}/{fileName}";

    /// <summary>Dosyayı denetleyip kaydeder. Başarılıysa yeni dosya adı, değilse hatanın dil anahtarı döner.</summary>
    public async Task<(string? FileName, string? ErrorKey)> SaveAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is 0 or > MaxBytes)
            return (null, "events.posterTooLarge");

        // ---- Türü ilk baytlardan anla ----
        var head = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        var extension = DetectExtension(head.AsSpan(0, read));
        if (extension is null)
            return (null, "events.posterInvalid");

        // ---- Kaydet ----
        var fileName = $"{Guid.NewGuid():N}{extension}";
        stream.Position = 0;
        await using (var target = File.Create(Path.Combine(Root, fileName)))
            await stream.CopyToAsync(target, cancellationToken);
        return (fileName, null);
    }

    /// <summary>Artık kullanılmayan afişi siler (afiş değiştirildiğinde ya da kaldırıldığında).</summary>
    public void Delete(string? fileName)
    {
        // Ad her zaman bizim ürettiğimiz biçimdedir; yine de klasör dışına çıkan bir ad silinmesin.
        if (string.IsNullOrEmpty(fileName) || fileName != Path.GetFileName(fileName))
            return;
        File.Delete(Path.Combine(Root, fileName));
    }

    private static string? DetectExtension(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith((byte[])[0xFF, 0xD8, 0xFF]))
            return ".jpg";
        if (head.StartsWith((byte[])[0x89, (byte)'P', (byte)'N', (byte)'G']))
            return ".png";
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
            return ".webp";
        return null;
    }
}
