using System;

namespace LanShare.Models;

[Flags]
public enum FilePermission
{
    None = 0,
    Read = 1,
    Write = 2,
    Delete = 4
}
