using System.Collections.Generic;

namespace LanShare.Models;

public sealed class PermissionRule
{
    public string UserName { get; set; } = string.Empty;

    public string DirectoryPath { get; set; } = string.Empty;

    public PermissionRuleEffect Effect { get; set; } = PermissionRuleEffect.Allow;

    public FilePermission Permissions { get; set; } = FilePermission.None;

    public bool InheritToChildren { get; set; } = true;

    public string DirectoryLabel => string.IsNullOrWhiteSpace(DirectoryPath) ? "/" : DirectoryPath.Replace('\\', '/');

    public string EffectLabel => Effect == PermissionRuleEffect.Allow ? "允许" : "拒绝";

    public string ScopeLabel => InheritToChildren ? "当前目录及子目录" : "仅当前目录";

    public string PermissionsLabel
    {
        get
        {
            if (Permissions == FilePermission.None)
            {
                return "无";
            }

            var values = new List<string>();

            if ((Permissions & FilePermission.Read) == FilePermission.Read)
            {
                values.Add("Read");
            }

            if ((Permissions & FilePermission.Write) == FilePermission.Write)
            {
                values.Add("Write");
            }

            if ((Permissions & FilePermission.Delete) == FilePermission.Delete)
            {
                values.Add("Delete");
            }

            return string.Join(", ", values);
        }
    }
}
