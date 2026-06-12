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
                store.NormalizeProviderProtocols();
                return store;
            }

            var legacy = JsonSerializer.Deserialize<AiSettings>(json, AiClient.JsonOptions) ?? new AiSettings();
            if (string.IsNullOrWhiteSpace(legacy.Id))
                legacy.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(legacy.DisplayName))
                legacy.DisplayName = "默认模型";
            NormalizeProviderProtocol(legacy);

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
        NormalizeProviderProtocols();
        var json = JsonSerializer.Serialize(this, AiClient.JsonOptions);
        File.WriteAllText(AiSettings.ConfigPath, json);
    }

    private AiSettings? Find(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : Profiles.FirstOrDefault(x => x.Id == id);

    private void NormalizeProviderProtocols()
    {
        foreach (var profile in Profiles)
            NormalizeProviderProtocol(profile);
    }

    private static void NormalizeProviderProtocol(AiSettings settings)
    {
        NormalizeArkCodingEndpoint(settings);

        if (settings.ProviderKey != "anthropic" || !LooksLikeOpenAICompatibleEndpoint(settings.ResolvedEndpoint))
            return;

        Logger.Warning($"AI 配置协议自动修正: provider=anthropic, endpoint={settings.ResolvedEndpoint} → OpenAI 兼容协议");
        settings.ProviderKey = settings.ResolvedEndpoint.Contains("ark.cn-beijing.volces.com", StringComparison.OrdinalIgnoreCase)
            ? "volcengine-ark"
            : "custom";
    }

    private static void NormalizeArkCodingEndpoint(AiSettings settings)
    {
        var endpoint = settings.Endpoint?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint)
            || !endpoint.Contains("ark.cn-beijing.volces.com", StringComparison.OrdinalIgnoreCase))
            return;

        var normalized = endpoint.TrimEnd('/');
        string? fixedEndpoint = null;
        if (normalized.EndsWith("/api/coding", StringComparison.OrdinalIgnoreCase))
            fixedEndpoint = normalized + "/v3";
        else if (normalized.EndsWith("/api/coding/chat/completions", StringComparison.OrdinalIgnoreCase))
            fixedEndpoint = normalized.Replace("/api/coding/chat/completions", "/api/coding/v3/chat/completions", StringComparison.OrdinalIgnoreCase);

        if (fixedEndpoint == null || string.Equals(endpoint, fixedEndpoint, StringComparison.OrdinalIgnoreCase))
            return;

        Logger.Warning($"火山方舟 Coding Plan endpoint 自动修正: {endpoint} → {fixedEndpoint}");
        settings.Endpoint = fixedEndpoint;
    }

    private static bool LooksLikeOpenAICompatibleEndpoint(string endpoint)
        => endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("/compatible-mode/", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("/openai/", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("ark.cn-beijing.volces.com", StringComparison.OrdinalIgnoreCase);

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
