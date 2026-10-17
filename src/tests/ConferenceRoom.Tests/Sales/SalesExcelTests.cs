using ClosedXML.Excel;
using ConferenceRoom.Domain.Accounts;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Tests.Auth;
using ConferenceRoom.Tests.Database;

namespace ConferenceRoom.Tests.Sales;

/// <summary>
/// Satış raporunun Excel'e aktarılması (gerçek MySQL): Web/Sales/SalesExcel.cs, Pages/Yonetim/Satislar ("?handler=Excel").
/// </summary>
[Collection(MySqlCollection.Name)]
public class SalesExcelTests(MySqlWebAppFactory factory) : IClassFixture<MySqlWebAppFactory>
{
    [MySqlFact]
    public async Task Rapor_goren_kullanici_seansin_islemlerini_ve_seans_listesini_excel_olarak_indirir()
    {
        var sessionId = await SalesTestData.CreateSessionAsync(factory, SaleMode.Sale);
        var seller = await factory.SignInSellerAsync();
        await SalesTestData.SellAsync(seller, sessionId, new { seats = new[] { "s1", "s2" }, buyerName = "Ali Veli", buyerPhone = "0532 000 00 00", paid = false });

        var (email, _) = await factory.CreateStaffInRoleAsync(Role.NotifierId);
        var notifier = await factory.SignInStaffAsync(email, MySqlWebAppFactory.StaffPassword);

        // Seansın işlemleri: başlık 4. satırda, işlem 5. satırda, toplam 6. satırda.
        using (var book = await DownloadAsync(notifier, $"/yonetim/satislar/seans/{sessionId}?handler=Excel"))
        {
            var sheet = book.Worksheet(1);
            Assert.Equal("Alıcı", sheet.Cell(4, 4).GetString());
            Assert.Equal("Ali Veli", sheet.Cell(5, 4).GetString());
            Assert.Equal("0532 000 00 00", sheet.Cell(5, 5).GetString());
            Assert.Equal("A-1, A-2", sheet.Cell(5, 6).GetString());
            Assert.Equal(500m, sheet.Cell(5, 8).GetValue<decimal>());
            Assert.Equal("Alınmadı", sheet.Cell(5, 9).GetString());
            Assert.Equal("Toplam", sheet.Cell(6, 1).GetString());
            Assert.Equal("SUM(H5:H5)", sheet.Cell(6, 8).FormulaA1);
        }

        // Seans listesi: seansın satırında satılan koltuk ve bekleyen ödeme.
        using (var book = await DownloadAsync(notifier, "/yonetim/satislar?handler=Excel&gecmis=true"))
        {
            var sheet = book.Worksheet(1);
            Assert.Equal("Etkinlik", sheet.Cell(1, 1).GetString());
            Assert.Contains(sheet.RowsUsed().Skip(1), r => r.Cell(1).GetString() == "Koltuk testi"
                && r.Cell(6).GetValue<int>() == 2 && r.Cell(10).GetValue<decimal>() == 500m);
        }
    }

    private static async Task<XLWorkbook> DownloadAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        return new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
    }
}
