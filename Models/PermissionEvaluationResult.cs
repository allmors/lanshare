using System;
using System.Collections.Generic;

namespace LanShare.Models;

public sealed class PermissionEvaluationResult
{
    public string UserName { get; init; } = string.Empty;

    public string DirectoryPath { get; init; } = string.Empty;

    public FilePermission EffectivePermissions { get; init; } = FilePermission.None;

    public bool UserExists { get; init; }

    public IReadOnlyList<string> AppliedRules { get; init; } = Array.Empty<string>();

    public string EffectivePermissionsText
    {
        get
        {
            if (EffectivePermissions == FilePermission.None)
            {
                return "无权限";
            }

            var values = new List<string>();
            if (IsAllowed(FilePermission.Read))
            {
                values.Add("Read");
            }

            if (IsAllowed(FilePermission.Write))
            {
                values.Add("Write");
            }

            if (IsAllowed(FilePermission.Delete))
            {
                values.Add("Delete");
            }

            return string.Join(", ", values);
        }
    }

    public bool IsAllowed(FilePermission permission)
    {
        return (EffectivePermissions & permission) == permission;
    }
}
