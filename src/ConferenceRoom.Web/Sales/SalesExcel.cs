using ClosedXML.Excel;
using ConferenceRoom.Domain.Events;
using ConferenceRoom.Domain.Sales;
using ConferenceRoom.Web.Common;
using Microsoft.Extensions.Localization;

namespace ConferenceRoom.Web.Sales;

/// <summary>
/// Satış raporunu Excel (.xlsx) dosyası olarak üretir (ClosedXML). Ekrandaki raporla aynı veriyi kullanır (<see cref="SalesReport"/>):
///   - <see cref="Sessions"/>: rapor listesi, her satır bir seans (/yonetim/satislar?handler=Excel)
///   - <see cref="Orders"/>: bir seansın işlemleri (/yonetim/satislar/seans/{id}?handler=Excel)
/// Başlıklar dil dosyasının "report" bölümünden gelir. Zamanlar Türkiye saatiyle, tutarlar sayı olarak yazılır ki Excel'de
/// toplanıp süzülebilsin; son satırda toplamlar formülle hesaplanır.
/// </summary>
public static class SalesExcel
{
    /// <summary>Dosyanın tarayıcıya bildirilen türü.</summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string DateFormat = "dd.MM.yyyy HH:mm";
    private const string MoneyFormat = "#,##0.00";
    private const string CountFormat = "#,##0";

    // ---- Rapor listesi: seans başına satılabilir, satılan, rezerve, kalan koltuk ve ödemeler ----

    public static byte[] Sessions(IReadOnlyList<SalesReport.SessionRow> sessions, IStringLocalizer T)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(T["report.sheetSessions"]);
        string[] headers = [T["report.event"], T["report.session"], T["report.hall"], T["report.status"], T["report.capacity"],
            T["report.sold"], T["report.reserved"], T["report.remaining"], T["report.received"], T["report.pending"]];
        WriteHeader(sheet, 1, headers);

        var r = 2;
        foreach (var s in sessions)
        {
            sheet.Cell(r, 1).Value = s.EventTitle;
            sheet.Cell(r, 2).Value = DisplayTime.ToLocal(s.StartsAtUtc);
            sheet.Cell(r, 3).Value = s.HallName;
            sheet.Cell(r, 4).Value = s.Status == EventStatus.Cancelled ? T["report.cancelled"].Value : s.Ended ? T["report.ended"].Value : "";
            sheet.Cell(r, 5).Value = s.Capacity;
            sheet.Cell(r, 6).Value = s.Sold;
            sheet.Cell(r, 7).Value = s.Reserved;
            sheet.Cell(r, 8).Value = s.Remaining;
            sheet.Cell(r, 9).Value = s.Received;
            sheet.Cell(r, 10).Value = s.Pending;
            r++;
        }
        sheet.Range(1, 1, Math.Max(r - 1, 1), headers.Length).SetAutoFilter();
        WriteTotals(sheet, r, T, sumColumns: [5, 6, 7, 8, 9, 10]);

        sheet.Column(2).Style.NumberFormat.Format = DateFormat;
        sheet.Columns(5, 8).Style.NumberFormat.Format = CountFormat;
        sheet.Columns(9, 10).Style.NumberFormat.Format = MoneyFormat;
        return Finish(book, sheet, [32, 17, 18, 10, 12, 10, 10, 10, 15, 15]);
    }

    // ---- Bir seansın işlemleri: üstte etkinlik ve seans, altında işlem tablosu ----

    public static byte[] Orders(SalesReport.SessionRow session, IReadOnlyList<SalesReport.OrderRow> orders, IStringLocalizer T)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(T["report.sheetOrders"]);
        sheet.Cell(1, 1).Value = session.EventTitle;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value = $"{DisplayTime.Format(session.StartsAtUtc)} · {session.HallName}";

        string[] headers = [T["report.orderNoColumn"], T["report.when"], T["report.kind"], T["report.buyer"], T["report.phone"],
            T["report.seats"], T["report.seatCount"], T["report.total"], T["report.payment"], T["report.seller"], T["report.note"]];
        const int headerRow = 4;
        WriteHeader(sheet, headerRow, headers);

        var r = headerRow + 1;
        foreach (var o in orders)
        {
            sheet.Cell(r, 1).Value = o.Id;
            sheet.Cell(r, 2).Value = DisplayTime.ToLocal(o.CreatedAtUtc);
            sheet.Cell(r, 3).Value = T[o.Kind == OrderKind.Sale ? "report.kindSale" : "report.kindReservation"].Value;
            sheet.Cell(r, 4).Value = o.BuyerName;
            sheet.Cell(r, 5).Value = o.BuyerPhone ?? "";
            sheet.Cell(r, 6).Value = string.Join(", ", o.Seats);
            sheet.Cell(r, 7).Value = o.Seats.Count;
            sheet.Cell(r, 8).Value = o.Total;
            sheet.Cell(r, 9).Value = T[$"report.payment{o.Payment}"].Value;
            sheet.Cell(r, 10).Value = o.SellerName ?? T["report.online"].Value;
            sheet.Cell(r, 11).Value = o.Note ?? "";
            r++;
        }
        sheet.Range(headerRow, 1, Math.Max(r - 1, headerRow), headers.Length).SetAutoFilter();
        WriteTotals(sheet, r, T, sumColumns: [7, 8], firstDataRow: headerRow + 1);

        sheet.Column(2).Style.NumberFormat.Format = DateFormat;
        sheet.Column(7).Style.NumberFormat.Format = CountFormat;
        sheet.Column(8).Style.NumberFormat.Format = MoneyFormat;
        sheet.Column(6).Style.Alignment.WrapText = true;
        return Finish(book, sheet, [10, 17, 13, 26, 16, 40, 12, 13, 12, 20, 30]);
    }

    // ---- Ortak: başlık satırı, toplam satırı, sütun genişlikleri, dosyaya yazma ----

    private static void WriteHeader(IXLWorksheet sheet, int row, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(row, i + 1).Value = headers[i];
        var range = sheet.Range(row, 1, row, headers.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF1F5");
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        sheet.SheetView.FreezeRows(row);
    }

    /// <summary>Tablonun altına "Toplam" satırı: verilen sütunlarda SUM formülü (Excel'de satır silinince toplam da değişir).</summary>
    private static void WriteTotals(IXLWorksheet sheet, int row, IStringLocalizer T, int[] sumColumns, int firstDataRow = 2)
    {
        sheet.Cell(row, 1).Value = T["report.totalRow"].Value;
        foreach (var c in sumColumns)
        {
            var letter = XLHelper.GetColumnLetterFromNumber(c);
            sheet.Cell(row, c).FormulaA1 = row > firstDataRow ? $"SUM({letter}{firstDataRow}:{letter}{row - 1})" : "0";
        }
        var range = sheet.Range(row, 1, row, sumColumns.Max());
        range.Style.Font.Bold = true;
        range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
    }

    /// <summary>
    /// Sütun genişlikleri sabit verilir (karakter cinsinden): AdjustToContents yazı tipi ölçümüne dayanır, yazı tipi olmayan
    /// Docker imajında güvenilmez.
    /// </summary>
    private static byte[] Finish(XLWorkbook book, IXLWorksheet sheet, double[] widths)
    {
        for (var i = 0; i < widths.Length; i++)
            sheet.Column(i + 1).Width = widths[i];
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }
}
