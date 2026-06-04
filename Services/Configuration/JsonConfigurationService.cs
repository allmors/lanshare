using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LanShare.Models;

namespace LanShare.Services.Configuration;

public sealed class JsonConfigurationService : IConfigurationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly string _configPath;

    public JsonConfigurationService(string configPath)
    {
        _configPath = configPath;
    }

    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            var defaultConfig = LoadBundledConfigOrDefault();
            Save(defaultConfig);
            return defaultConfig;
        }

        var json = File.ReadAllText(_configPath);
        return JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? CreateDefaultConfig();
    }

    public void Save(AppConfig config)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(_configPath, json);
    }

    private static AppConfig LoadBundledConfigOrDefault()
    {
        var bundledConfigPath = AppPaths.GetBundledDefaultConfigPath();
        if (!File.Exists(bundledConfigPath))
        {
            return CreateDefaultConfig();
        }

        try
        {
            var json = File.ReadAllText(bundledConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? CreateDefaultConfig();
        }
        catch
        {
            return CreateDefaultConfig();
        }
    }

    private static AppConfig CreateDefaultConfig()
    {
        return new AppConfig
        {
            AdminAccessKey = "LanShareAdmin",
            Server = new ServerConfig
            {
                SharedFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            },
            Client = new ClientConfig
            {
                BuiltInServerHost = "192.168.202.163",
                BuiltInServerPort = 49443,
                PreferredServerAddress = string.Empty,
                AutoConnectPreferredServerOnStartup = true,
                DownloadFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads",
                    "LanShare")
            },
            Permissions = new PermissionConfig
            {
                Users =
                {
                    new UserAccount
                    {
                        UserName = "admin",
                        DisplayName = "Administrator"
                    },
                    new UserAccount
                    {
                        UserName = "guest",
                        DisplayName = "Guest"
                    }
                },
                Rules =
                {
                    new PermissionRule
                    {
                        UserName = "admin",
                        DirectoryPath = string.Empty,
                        Effect = PermissionRuleEffect.Allow,
                        Permissions = FilePermission.Read | FilePermission.Write | FilePermission.Delete,
                        InheritToChildren = true
                    },
                    new PermissionRule
                    {
                        UserName = "guest",
                        DirectoryPath = string.Empty,
                        Effect = PermissionRuleEffect.Allow,
                        Permissions = FilePermission.Read,
                        InheritToChildren = true
                    },
                    new PermissionRule
                    {
                        UserName = "guest",
                        DirectoryPath = "private",
                        Effect = PermissionRuleEffect.Deny,
                        Permissions = FilePermission.Read,
                        InheritToChildren = true
                    }
                }
            }
        };
    }
}
