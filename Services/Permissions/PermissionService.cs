using System;
using System.Collections.Generic;
using System.Linq;
using LanShare.Models;

namespace LanShare.Services.Permissions;

public sealed class PermissionService : IPermissionService
{
    public PermissionEvaluationResult Evaluate(
        PermissionConfig config,
        string userName,
        string directoryPath)
    {
        var normalizedUserName = NormalizeUserName(userName);
        var normalizedDirectoryPath = NormalizeDirectoryPath(directoryPath);

        var matchingUser = config.Users.FirstOrDefault(user =>
            string.Equals(user.UserName, normalizedUserName, StringComparison.OrdinalIgnoreCase) &&
            user.IsEnabled);

        if (matchingUser is null)
        {
            return new PermissionEvaluationResult
            {
                UserName = normalizedUserName,
                DirectoryPath = normalizedDirectoryPath,
                EffectivePermissions = FilePermission.None,
                UserExists = false
            };
        }

        var applicableRules = config.Rules
            .Where(rule => string.Equals(rule.UserName, normalizedUserName, StringComparison.OrdinalIgnoreCase))
            .Where(rule => RuleApplies(rule, normalizedDirectoryPath))
            .Select(rule => new RuleMatch(
                rule,
                NormalizeDirectoryPath(rule.DirectoryPath),
                IsExactMatch(rule, normalizedDirectoryPath)))
            .OrderByDescending(match => GetDepth(match.NormalizedPath))
            .ThenByDescending(match => match.IsExactMatch)
            .ThenBy(match => match.Rule.Effect == PermissionRuleEffect.Deny ? 0 : 1)
            .ToArray();

        var effectivePermissions = FilePermission.None;
        var appliedRules = new List<string>();

        foreach (var permission in new[] { FilePermission.Read, FilePermission.Write, FilePermission.Delete })
        {
            var decision = applicableRules.FirstOrDefault(match => (match.Rule.Permissions & permission) == permission);
            if (decision is null)
            {
                continue;
            }

            appliedRules.Add(
                $"{decision.Rule.UserName} | {(string.IsNullOrWhiteSpace(decision.NormalizedPath) ? "/" : decision.NormalizedPath)} | {decision.Rule.EffectLabel} | {permission}");

            if (decision.Rule.Effect == PermissionRuleEffect.Allow)
            {
                effectivePermissions |= permission;
            }
        }

        return new PermissionEvaluationResult
        {
            UserName = normalizedUserName,
            DirectoryPath = normalizedDirectoryPath,
            EffectivePermissions = effectivePermissions,
            UserExists = true,
            AppliedRules = appliedRules
        };
    }

    public bool HasPermission(
        PermissionConfig config,
        string userName,
        string directoryPath,
        FilePermission permission)
    {
        return Evaluate(config, userName, directoryPath).IsAllowed(permission);
    }

    private static bool RuleApplies(PermissionRule rule, string directoryPath)
    {
        var normalizedRulePath = NormalizeDirectoryPath(rule.DirectoryPath);
        if (string.Equals(normalizedRulePath, directoryPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!rule.InheritToChildren)
        {
            return false;
        }

        if (string.IsNullOrEmpty(normalizedRulePath))
        {
            return true;
        }

        return directoryPath.StartsWith(normalizedRulePath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExactMatch(PermissionRule rule, string directoryPath)
    {
        return string.Equals(
            NormalizeDirectoryPath(rule.DirectoryPath),
            directoryPath,
            StringComparison.OrdinalIgnoreCase);
    }

    private static int GetDepth(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? 0
            : path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string NormalizeUserName(string userName)
    {
        return string.IsNullOrWhiteSpace(userName)
            ? string.Empty
            : userName.Trim();
    }

    private static string NormalizeDirectoryPath(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return string.Empty;
        }

        var segments = directoryPath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        var stack = new Stack<string>();
        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (stack.Count > 0)
                {
                    stack.Pop();
                }

                continue;
            }

            stack.Push(segment);
        }

        return string.Join("/", stack.Reverse());
    }

    private sealed record RuleMatch(PermissionRule Rule, string NormalizedPath, bool IsExactMatch);
}
