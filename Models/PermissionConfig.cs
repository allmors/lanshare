using System.Collections.Generic;

namespace LanShare.Models;

public sealed class PermissionConfig
{
    public List<UserAccount> Users { get; set; } = new();

    public List<PermissionRule> Rules { get; set; } = new();
}
