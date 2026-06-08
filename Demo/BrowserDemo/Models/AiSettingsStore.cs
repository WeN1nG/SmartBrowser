using System.IO;
using System.Text.Json;
using BrowserDemo.Services;

namespace BrowserDemo.Models;

public class AiSettingsStore
{
    public List<AiSettings> Profiles { get; set; } = new();
    public string? ActiveId { get; set; }
    public string? DefaultId { get; set; }

    public AiSettings ResolveActive()
    {
        EnsureProfileIds();
        if (Profiles.Count == 0) return new AiSettings();

        return Find(ActiveId) ?? Find(DefaultId) ?? Profiles[0];
    }

    public void Upsert(AiSettings settings, bool setActive = true)
    {
        if (string.IsNullOrWhiteSpace(settings.Id))
            settings.Id = Guid.NewGuid().ToString("N");

        var index = Profiles.FindIndex(x => x.Id == settings.Id);
        if (index >= 0)
            Profiles[index] = settings;
        else
            Profiles.Add(settings);

        if (setActive)
            ActiveId = settings.Id;
    }

    public static AiSettingsStore Load()
    {
        try
        {
            if (!File.Exists(AiSettings.ConfigPath))
                return new AiSettingsStore();

            var json = File.ReadAllText(AiSettings.ConfigPath);
            if (string.IsNullOrWhiteSpace(json))
                return new AiSettingsStore();

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("profiles", out _))
            {
                var store = JsonSerializer.Deserialize<AiSettingsStore>(json, AiClient.JsonOptions) ?? new AiSettingsStore();
                store.EnsureProfileIds();
                return store;
            }

            var legacy = JsonSerializer.Deserialize<AiSettings>(json, AiClient.JsonOptions) ?? new AiSettings();
            if (string.IsNullOrWhiteSpace(legacy.Id))
                legacy.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(legacy.DisplayName))
                legacy.DisplayName = "默认模型";

            var migrated = new AiSettingsStore
            {
                Profiles = [legacy],
                ActiveId = legacy.Id,
                DefaultId = legacy.Id
            };
            migrated.Save();
            Logger.Info("已将旧 AI 设置迁移为多模型配置");
            return migrated;
        }
        catch (Exception ex)
        {
            Logger.Warning($"加载 AI 多模型配置失败: {ex.Message}");
            return new AiSettingsStore();
        }
    }

    public void Save()
    {
        EnsureProfileIds();
        var json = JsonSerializer.Serialize(this, AiClient.JsonOptions);
        File.WriteAllText(AiSettings.ConfigPath, json);
    }

    private AiSettings? Find(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : Profiles.FirstOrDefault(x => x.Id == id);

    private void EnsureProfileIds()
    {
        foreach (var profile in Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
                profile.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(profile.DisplayName))
                profile.DisplayName = profile.Model;
        }
    }
}
