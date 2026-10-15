using System.Text.RegularExpressions;

namespace ConferenceRoom.Tests.Auth;

/// <summary>Formları tarayıcı gibi gönderir: önce sayfa açılır, sahtecilik önleme anahtarı formdan alınır.</summary>
public static partial class Forms
{
    /// <summary>
    /// <paramref name="url"/> sayfasını açar, formu alanlarla gönderir ve son sayfanın HTML'ini döner (yönlendirmeler izlenir).
    /// Aynı adla birden çok değer (işaretli kutular) için alanlar liste olarak verilebilir.
    /// <paramref name="postUrl"/> verilirse form oraya gönderilir (ör. silme düğmesi: "...?handler=Delete").
    /// </summary>
    public static async Task<string> PostAsync(HttpClient client, string url, IEnumerable<KeyValuePair<string, string>> fields, string? postUrl = null)
    {
        var page = await client.GetStringAsync(url);
        var token = TokenPattern().Match(page).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "Formda sahtecilik önleme anahtarı bulunamadı.");
        var content = new FormUrlEncodedContent(fields.Append(KeyValuePair.Create("__RequestVerificationToken", token)));
        var response = await client.PostAsync(postUrl ?? url, content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
