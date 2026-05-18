using UglyToad.PdfPig;

namespace DfE.CheckPerformanceData.Web.FileStorage;

public static class PdfPageCounter
{
    public static int? GetPageCount(byte[] bytes)
    {
        try
        {
            using var doc = PdfDocument.Open(bytes);
            return doc.NumberOfPages;
        }
        catch
        {
            return null;
        }
    }
}
