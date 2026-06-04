using System;
using System.IO;

namespace LanShare.Services.Configuration;

public static class AppPaths
{
    private const string AppFolderName = "LanShare";
    private const string ConfigFileName = "lanshare.json";

    public static string GetAppDataDirectory()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataRoot, AppFolderName);
    }

    public static string GetUserConfigPath()
    {
        return Path.Combine(GetAppDataDirectory(), ConfigFileName);
    }

    public static string GetBundledDefaultConfigPath()
    {
        return Path.Combine(AppContext.BaseDirectory, ConfigFileName);
    }
}
