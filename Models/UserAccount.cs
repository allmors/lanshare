namespace LanShare.Models;

public sealed class UserAccount
{
    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
}
