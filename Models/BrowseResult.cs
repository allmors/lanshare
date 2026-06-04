using System.Collections.Generic;

namespace LanShare.Models;

public sealed class BrowseResult
{
    public IReadOnlyList<BrowseEntry> Entries { get; set; } = [];

    public bool CanWriteCurrentDirectory { get; set; }
}
