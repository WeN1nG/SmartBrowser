using System.Text.Json.Serialization;

namespace BrowserDemo.Models;

/// <summary>AI 设置（API Key、模型、端点、提供商）</summary>
public class AiSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "默认模型";

    /// <summary>提供商唯一标识（如 "openai", "anthropic", "groq"）</summary>
    public string ProviderKey { get; set; } = "openai";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>获取实际 API 端点</summary>
    [JsonIgnore]
    public string ResolvedEndpoint
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Endpoint)) return Endpoint;
            var info = ProviderManager.GetProvider(ProviderKey);
            return info?.DefaultEndpoint ?? "https://api.openai.com/v1/chat/completions";
        }
    }

    /// <summary>默认模型名</summary>
    [JsonIgnore]
    public string DefaultModel
    {
        get
        {
            var info = ProviderManager.GetProvider(ProviderKey);
            if (info != null && info.Models.Count > 0)
                return info.Models[0].Id;
            return "gpt-4o";
        }
    }

    /// <summary>是否已配置密钥</summary>
    [JsonIgnore]
    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>配置文件路径</summary>
    [JsonIgnore]
    public static readonly string ConfigPath = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "ai_settings.json");
}
