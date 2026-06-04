using LanShare.Models;

namespace LanShare.Services.Permissions;

public interface IPermissionService
{
    PermissionEvaluationResult Evaluate(
        PermissionConfig config,
        string userName,
        string directoryPath);

    bool HasPermission(
        PermissionConfig config,
        string userName,
        string directoryPath,
        FilePermission permission);
}
