using System.Collections.ObjectModel;
using BrowserDemo.Services;

namespace BrowserDemo.Models;

/// <summary>AI 提供商元数据（名称、端点、认证方式、模型列表）</summary>
public class ProviderInfo
{
    /// <summary>唯一标识符</summary>
    public string Key { get; set; } = "";
    /// <summary>显示名称</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>默认 API 端点</summary>
    public string DefaultEndpoint { get; set; } = "";
    /// <summary>API 认证方式描述</summary>
    public string AuthType { get; set; } = "Bearer";
    /// <summary>备注/标识</summary>
    public string Badge { get; set; } = "";
    /// <summary>该提供商支持的模型列表</summary>
    public List<ModelInfo> Models { get; set; } = new();
}

/// <summary>模型元数据</summary>
public class ModelInfo
{
    public ModelInfo() { }

    public ModelInfo(string id, string displayName, int contextK, string tags)
    {
        Id = id;
        DisplayName = displayName;
        ContextK = contextK;
        Tags = tags;
    }

    /// <summary>模型 ID（API 请求时使用）</summary>
    public string Id { get; set; } = "";
    /// <summary>显示名称</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>上下文窗口大小（K）</summary>
    public int ContextK { get; set; }
    /// <summary>能力标签：chat / reasoning / vision / code</summary>
    public string Tags { get; set; } = "";

    public string Summary => $"{DisplayName} ({ContextK}K, {Tags})";
}

/// <summary>供应商注册中心——管理所有已知的 AI 服务商和模型</summary>
public static class ProviderManager
{
    private static readonly Dictionary<string, ProviderInfo> Providers = new();

    static ProviderManager()
    {
        RegisterAll();
        Logger.Info($"ProviderManager: 已注册 {Providers.Count} 个服务商，共 {Providers.Values.Sum(p => p.Models.Count)} 个模型");
    }

    /// <summary>获取所有提供商（按显示顺序）</summary>
    public static ObservableCollection<ProviderInfo> GetAll()
    {
        var order = new[] {
            "openai", "anthropic", "google", "deepseek", "volcengine-ark", "xai",
            "groq", "cerebras", "mistral", "togetherai", "fireworks",
            "openrouter", "alibaba", "zhipu", "moonshot", "siliconflow",
            "ollama", "custom"
        };
        var list = new ObservableCollection<ProviderInfo>();
        foreach (var key in order)
            if (Providers.TryGetValue(key, out var p))
                list.Add(p);
        // 其他不在 order 中的
        foreach (var kv in Providers)
            if (!order.Contains(kv.Key))
                list.Add(kv.Value);
        return list;
    }

    /// <summary>按 Key 获取提供商信息</summary>
    public static ProviderInfo? GetProvider(string key)
        => Providers.TryGetValue(key, out var p) ? p : null;

    /// <summary>获取提供商默认模型列表</summary>
    public static List<ModelInfo> GetModels(string providerKey)
        => GetProvider(providerKey)?.Models ?? new();

    // =================================================================
    // 注册所有已知的 AI 服务商
    // =================================================================
    private static void RegisterAll()
    {
        Register("openai", "OpenAI", "https://api.openai.com/v1/chat/completions", "Bearer", "官方",
            new ModelInfo("gpt-4o",              "GPT-4o",              128, "chat,vision"),
            new ModelInfo("gpt-4o-mini",         "GPT-4o Mini",         128, "chat,vision"),
            new ModelInfo("gpt-5",               "GPT-5",               256, "chat,vision,reasoning"),
            new ModelInfo("gpt-5.1-instant",     "GPT-5.1 Instant",     256, "chat,vision,reasoning"),
            new ModelInfo("gpt-5.1-thinking",    "GPT-5.1 Thinking",    256, "reasoning"),
            new ModelInfo("o1",                  "o1",                  200, "reasoning"),
            new ModelInfo("o3-mini",             "o3 Mini",             200, "reasoning"),
            new ModelInfo("text-embedding-3-large", "Embedding 3 Large", 8, "embedding")
        );

        Register("anthropic", "Anthropic", "https://api.anthropic.com/v1/messages", "x-api-key", "官方",
            new ModelInfo("claude-sonnet-4-6",    "Claude Sonnet 4.6",   200, "chat,vision,reasoning"),
            new ModelInfo("claude-opus-4-8",     "Claude Opus 4.8",     200, "chat,vision,reasoning"),
            new ModelInfo("claude-haiku-4-5",    "Claude Haiku 4.5",    200, "chat,vision"),
            new ModelInfo("claude-3-5-sonnet",   "Claude 3.5 Sonnet",   200, "chat,vision"),
            new ModelInfo("claude-3-5-haiku",    "Claude 3.5 Haiku",    200, "chat,vision")
        );

        Register("google", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", "Bearer", "免费额度",
            new ModelInfo("gemini-2-5-flash",     "Gemini 2.5 Flash",    1048, "chat,vision"),
            new ModelInfo("gemini-2-5-pro",       "Gemini 2.5 Pro",     1048, "chat,vision,reasoning"),
            new ModelInfo("gemini-2-0-flash",     "Gemini 2.0 Flash",   1048, "chat,vision")
        );

        Register("deepseek", "DeepSeek", "https://api.deepseek.com/v1/chat/completions", "Bearer", "极低价",
            new ModelInfo("deepseek-chat",        "DeepSeek V3",         64,  "chat,code"),
            new ModelInfo("deepseek-reasoner",    "DeepSeek R1",         64,  "reasoning,code")
        );

        Register("volcengine-ark", "Volcengine ARK Coding Plan（火山方舟）", "https://ark.cn-beijing.volces.com/api/coding/v3", "Bearer", "火山方舟");

        Register("xai", "xAI (Grok)", "https://api.x.ai/v1/chat/completions", "Bearer", "X 数据",
            new ModelInfo("grok-4",               "Grok 4",              256, "chat,vision,reasoning"),
            new ModelInfo("grok-3",               "Grok 3",              256, "chat,vision"),
            new ModelInfo("grok-2",               "Grok 2",              128, "chat,vision")
        );

        Register("groq", "Groq", "https://api.groq.com/openai/v1/chat/completions", "Bearer", "超快",
            new ModelInfo("llama-3-3-70b",        "Llama 3.3 70B",       128, "chat"),
            new ModelInfo("llama-guard-3-8b",     "Llama Guard 3 8B",    8,   "moderation"),
            new ModelInfo("mixtral-8x7b",         "Mixtral 8x7B",        32,  "chat")
        );

        Register("cerebras", "Cerebras", "https://api.cerebras.ai/v1/chat/completions", "Bearer", "超快",
            new ModelInfo("llama-3-3-70b",        "Llama 3.3 70B",       128, "chat")
        );

        Register("mistral", "Mistral AI", "https://api.mistral.ai/v1/chat/completions", "Bearer", "欧洲",
            new ModelInfo("mistral-large-latest", "Mistral Large 2",     128, "chat,code,multilingual"),
            new ModelInfo("mistral-small-latest", "Mistral Small",       32,  "chat"),
            new ModelInfo("codestral-latest",     "Codestral",           256, "code"),
            new ModelInfo("open-mistral-nemo",    "Mistral Nemo",        128, "chat")
        );

        Register("togetherai", "Together AI", "https://api.together.xyz/v1/chat/completions", "Bearer", "200+模型",
            new ModelInfo("meta-llama/Llama-3-3-70B", "Llama 3.3 70B",  128, "chat"),
            new ModelInfo("mistralai/Mixtral-8x7B",   "Mixtral 8x7B",   32,  "chat"),
            new ModelInfo("Qwen/Qwen2-72B",           "Qwen2 72B",      128, "chat"),
            new ModelInfo("deepseek-ai/DeepSeek-R1",  "DeepSeek R1",    64,  "reasoning")
        );

        Register("fireworks", "Fireworks AI", "https://api.fireworks.ai/inference/v1/chat/completions", "Bearer", "优化推理",
            new ModelInfo("accounts/fireworks/models/llama-v3p3-70b", "Llama 3.3 70B", 128, "chat"),
            new ModelInfo("accounts/fireworks/models/deepseek-r1",   "DeepSeek R1",   64,  "reasoning")
        );

        Register("openrouter", "OpenRouter", "https://openrouter.ai/api/v1/chat/completions", "Bearer", "聚合·500+模型",
            new ModelInfo("openai/gpt-4o",               "GPT-4o",            128, "chat,vision"),
            new ModelInfo("anthropic/claude-sonnet-4-6", "Claude Sonnet 4.6", 200, "chat,vision"),
            new ModelInfo("google/gemini-2-5-flash",     "Gemini 2.5 Flash",  1048,"chat,vision"),
            new ModelInfo("deepseek/deepseek-chat",      "DeepSeek V3",       64,  "chat"),
            new ModelInfo("xai/grok-4",                  "Grok 4",            256, "chat,vision"),
            new ModelInfo("meta-llama/llama-3-3-70b",    "Llama 3.3 70B",    128, "chat"),
            new ModelInfo("mistralai/mistral-large",     "Mistral Large",     128, "chat")
        );

        Register("alibaba", "Alibaba (Qwen)", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", "Bearer", "国产",
            new ModelInfo("qwen-max",             "Qwen3 Max",           128, "chat,vision"),
            new ModelInfo("qwen-plus",            "Qwen3 Plus",          128, "chat"),
            new ModelInfo("qwen-turbo",           "Qwen3 Turbo",         128, "chat")
        );

        Register("zhipu", "Zhipu AI (GLM)", "https://open.bigmodel.cn/api/paas/v4/chat/completions", "Bearer", "国产",
            new ModelInfo("glm-4-plus",           "GLM-4 Plus",          128, "chat,vision"),
            new ModelInfo("glm-4v-plus",          "GLM-4V Plus",         128, "vision")
        );

        Register("moonshot", "Moonshot (Kimi)", "https://api.moonshot.cn/v1/chat/completions", "Bearer", "国产",
            new ModelInfo("moonshot-v1-8k",       "Moonshot 8K",          8,  "chat"),
            new ModelInfo("moonshot-v1-32k",      "Moonshot 32K",        32,  "chat"),
            new ModelInfo("moonshot-v1-128k",     "Moonshot 128K",       128, "chat")
        );

        Register("siliconflow", "SiliconFlow", "https://api.siliconflow.cn/v1/chat/completions", "Bearer", "开源模型",
            new ModelInfo("deepseek-ai/DeepSeek-V3",   "DeepSeek V3",    64,  "chat"),
            new ModelInfo("Pro/deepseek-ai/DeepSeek-R1","DeepSeek R1",   64,  "reasoning"),
            new ModelInfo("meta-llama/Llama-3-3-70B",  "Llama 3.3 70B",  128, "chat"),
            new ModelInfo("Qwen/Qwen2-5-72B",          "Qwen2.5 72B",    128, "chat")
        );

        Register("ollama", "Ollama (本地)", "http://localhost:11434/v1/chat/completions", "Bearer", "本地",
            new ModelInfo("llama3.3",             "Llama 3.3",           128, "chat"),
            new ModelInfo("qwen2.5",              "Qwen 2.5",            128, "chat"),
            new ModelInfo("mistral",              "Mistral",             32,  "chat"),
            new ModelInfo("deepseek-r1",          "DeepSeek R1 (本地)",   64,  "reasoning")
        );

        Register("deepinfra", "DeepInfra", "https://api.deepinfra.com/v1/openai/chat/completions", "Bearer", "推理",
            new ModelInfo("meta-llama/Llama-3-3-70B", "Llama 3.3 70B", 128, "chat"),
            new ModelInfo("mistralai/Mixtral-8x7B",   "Mixtral 8x7B",  32,  "chat")
        );

        // 自定义 OpenAI 兼容服务商（无默认端点，无固定模型列表）
        Register("custom", "自定义（OpenAI 兼容）", "", "Bearer", "自定义");
    }

    private static void Register(string key, string displayName, string endpoint,
        string authType, string badge, params ModelInfo[] models)
    {
        Providers[key] = new ProviderInfo
        {
            Key = key,
            DisplayName = displayName,
            DefaultEndpoint = endpoint,
            AuthType = authType,
            Badge = badge,
            Models = models.ToList()
        };
        Logger.Debug($"注册提供商: {key} ({displayName}) — {models.Length} 个模型, 端点={endpoint}");
    }
}
