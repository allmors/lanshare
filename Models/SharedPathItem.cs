namespace LanShare.Models;

public sealed class SharedPathItem
{
    public string RelativePath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    public string DisplayPath => string.IsNullOrWhiteSpace(RelativePath) ? "/" : RelativePath;

    public string TypeLabel => IsDirectory ? "目录" : "文件";
}
