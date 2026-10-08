using System.Text;
using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace DfE.CheckPerformanceData.Web.Admin;

public enum ChunkedCommitStatus { Committed, Incomplete, TooLarge }

public sealed record ChunkedCommitOutcome(ChunkedCommitStatus Status, long Bytes);

/// <summary>
/// #568: a browser upload in parts, mapped one-to-one onto Azure block blobs. Each part is
/// staged as an uncommitted block under an id the server derives from the upload id and the
/// part index; the commit rebuilds the same id list and asks storage to assemble it.
/// </summary>
/// <remarks>
/// There is no server state. An abandoned upload leaves uncommitted blocks that storage
/// discards after seven days. Re-sending a commit is safe: the ids resolve to the committed
/// blocks and the blob is unchanged. The size check reads the block list rather than trusting
/// a declared total, so the configured maximum holds whatever the client claims.
/// </remarks>
public static class StorageChunkedUpload
{
    /// <summary>Base64 of "{uploadId:N}-{index:D6}": fixed width, as Put Block requires.</summary>
    public static string BlockId(Guid uploadId, int index) =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{uploadId:N}-{index:D6}"));

    public static IReadOnlyList<string> BlockIds(Guid uploadId, int blockCount) =>
        Enumerable.Range(0, blockCount).Select(i => BlockId(uploadId, i)).ToList();

    public static Task StageAsync(BlockBlobClient blob, Guid uploadId, int index, Stream content, CancellationToken ct) =>
        blob.StageBlockAsync(BlockId(uploadId, index), content, cancellationToken: ct);

    public static async Task<ChunkedCommitOutcome> CommitAsync(
        BlockBlobClient blob, Guid uploadId, int blockCount, long maxBytes, string? contentType, CancellationToken ct)
    {
        var ids = BlockIds(uploadId, blockCount);
        var wanted = new HashSet<string>(ids, StringComparer.Ordinal);

        BlockList list;
        try
        {
            list = (await blob.GetBlockListAsync(BlockListTypes.All, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Nothing staged under this name at all.
            return new ChunkedCommitOutcome(ChunkedCommitStatus.Incomplete, 0);
        }

        var found = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var block in list.UncommittedBlocks.Concat(list.CommittedBlocks))
        {
            if (wanted.Contains(block.Name)) found.TryAdd(block.Name, block.SizeLong);
        }

        if (found.Count < blockCount)
            return new ChunkedCommitOutcome(ChunkedCommitStatus.Incomplete, found.Values.Sum());

        var total = found.Values.Sum();
        if (total > maxBytes)
            return new ChunkedCommitOutcome(ChunkedCommitStatus.TooLarge, total);

        try
        {
            await blob.CommitBlockListAsync(ids, new CommitBlockListOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType ?? "application/octet-stream" }
            }, ct);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.InvalidBlockList)
        {
            // A block vanished between the list and the commit (another upload to the same name
            // committed first, which discards every other uncommitted block on the blob).
            return new ChunkedCommitOutcome(ChunkedCommitStatus.Incomplete, total);
        }

        return new ChunkedCommitOutcome(ChunkedCommitStatus.Committed, total);
    }
}
