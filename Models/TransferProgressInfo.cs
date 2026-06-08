namespace LanShare.Models;

public sealed class TransferProgressInfo
{
    public string Operation { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public long BytesTransferred { get; init; }

    public long? TotalBytes { get; init; }

    public long? OverallBytesTransferred { get; init; }

    public long? OverallTotalBytes { get; init; }

    public int FileIndex { get; init; }

    public int FileCount { get; init; }

    public bool IsCompleted { get; init; }
}
