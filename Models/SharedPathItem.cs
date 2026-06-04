namespace LanShare.Models;

public sealed class SharedPathItem
{
    public string RelativePath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    public string Name
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RelativePath))
            {
                return "/";
            }

            var normalized = RelativePath.TrimEnd('/');
            var index = normalized.LastIndexOf('/');
            return index >= 0 ? normalized[(index + 1)..] : normalized;
        }
    }

    public string DisplayPath => string.IsNullOrWhiteSpace(RelativePath) ? "/" : RelativePath;

    public string TypeLabel => IsDirectory ? "目录" : "文件";
}
