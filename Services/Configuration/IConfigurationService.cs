using LanShare.Models;

namespace LanShare.Services.Configuration;

public interface IConfigurationService
{
    AppConfig Load();

    void Save(AppConfig config);
}
