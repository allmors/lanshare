using System.Collections.Generic;

namespace LanShare.Models;

public sealed class BrowseEntry
{
    public string Name { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    public long Size { get; set; }

    public DateTimeOffset LastModified { get; set; }

    public bool CanRead { get; set; }

    public bool CanWrite { get; set; }

    public bool CanDelete { get; set; }

    public string EntryType => IsDirectory ? "目录" : "文件";

    public string SizeText => IsDirectory ? "--" : $"{Size:N0} B";

    public string LastModifiedText => LastModified == default ? "--" : LastModified.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string PermissionSummary
    {
        get
        {
            var values = new List<string>();

            if (CanRead)
            {
                values.Add("读");
            }

            if (CanWrite)
            {
                values.Add("写");
            }

            if (CanDelete)
            {
                values.Add("删");
            }

            return values.Count == 0 ? "--" : string.Join(" / ", values);
        }
    }
}
