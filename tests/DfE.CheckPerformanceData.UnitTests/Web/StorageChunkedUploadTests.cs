using DfE.CheckPerformanceData.Web.Admin;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class StorageChunkedUploadTests
{
    // Put Block requires every block id in a blob to have the same length, and the id must be
    // derived on the server so a request can never name a block it did not stage.
    [Fact]
    public void BlockIds_AreSameLength_Unique_AndOrdered()
    {
        var uploadId = Guid.NewGuid();

        var ids = StorageChunkedUpload.BlockIds(uploadId, 300);

        Assert.Equal(300, ids.Count);
        Assert.Equal(300, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Single(ids.Select(id => id.Length).Distinct());
        Assert.Equal(StorageChunkedUpload.BlockId(uploadId, 0), ids[0]);
        Assert.Equal(StorageChunkedUpload.BlockId(uploadId, 299), ids[299]);
        var decoded = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(ids[7]));
        Assert.Equal($"{uploadId:N}-000007", decoded);
    }

    [Fact]
    public void BlockIds_ForDifferentUploads_NeverCollide()
    {
        var a = StorageChunkedUpload.BlockIds(Guid.NewGuid(), 5);
        var b = StorageChunkedUpload.BlockIds(Guid.NewGuid(), 5);
        Assert.Empty(a.Intersect(b, StringComparer.Ordinal));
    }
}
