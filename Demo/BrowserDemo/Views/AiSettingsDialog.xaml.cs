using System.Windows;
using System.Windows.Controls;
using BrowserDemo.Models;
using BrowserDemo.Services;

namespace BrowserDemo.Views;

public partial class AiSettingsDialog : Window
{
    private readonly IAiClient _aiClient;
    private bool _saved;
    private bool _isUpdating;

    public AiSettingsDialog(IAiClient aiClient, Window owner)
    {
        using var _ = Logger.Trace("AiSettingsDialog::ctor");

        InitializeComponent();
        Owner = owner;
        _aiClient = aiClient;

        // 填充服务商列表
        ProviderCombo.ItemsSource = ProviderManager.GetAll();
        LoadSettings();

        Logger.Info("AI 设置对话框已打开");
    }

    private void LoadSettings()
    {
        var s = _aiClient.Settings;
        Logger.Debug($"加载当前设置: provider={s.ProviderKey}, model={s.Model}");

        // 选中当前服务商；旧配置若把 ARK 误存成 anthropic，则优先按 ARK 打开，避免继续使用错误协议。
        var providers = ProviderManager.GetAll();
        var providerKeyToSelect = LooksLikeArkSettings(s) ? "volcengine-ark" : s.ProviderKey;
        for (int i = 0; i < providers.Count; i++)
        {
            if (providers[i].Key == providerKeyToSelect)
            {
                ProviderCombo.SelectedIndex = i;
                break;
            }
        }

        if (ProviderCombo.SelectedIndex < 0 && !string.IsNullOrWhiteSpace(s.Endpoint))
        {
            for (int i = 0; i < providers.Count; i++)
            {
                if (providers[i].Key == "custom")
                {
                    ProviderCombo.SelectedIndex = i;
                    break;
                }
            }
        }

        if (ProviderCombo.SelectedIndex < 0 && providers.Count > 0)
            ProviderCombo.SelectedIndex = 0;

        DisplayNameBox.Text = s.DisplayName;
        ApiKeyBox.Password = s.ApiKey;
        EndpointBox.Text = s.Endpoint;
        if (ProviderCombo.SelectedItem is ProviderInfo { Key: "volcengine-ark" } arkProvider
            && (string.IsNullOrWhiteSpace(EndpointBox.Text)
                || (IsArkEndpoint(EndpointBox.Text) && !IsArkCodingPlanEndpoint(EndpointBox.Text))))
        {
            EndpointBox.Text = arkProvider.DefaultEndpoint;
        }

        UpdateModelList(s.Model);
    }

    /// <summary>服务商切换时更新模型列表、端点预览、认证方式</summary>
    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating) return;
        if (ProviderCombo.SelectedItem is not ProviderInfo provider) return;

        Logger.Info($"切换服务商: {provider.Key} ({provider.DisplayName})");

        _isUpdating = true;

        ProviderBadge.Text = provider.Badge;
        AuthTypeText.Text = provider.AuthType switch
        {
            "x-api-key" => "x-api-key 认证",
            _ => "Bearer Token"
        };
        EndpointPreview.Text = provider.DefaultEndpoint;

        UpdateModelList(null);

        var currentSettings = _aiClient.Settings;
        if (string.IsNullOrWhiteSpace(currentSettings.Endpoint)
            || currentSettings.Endpoint == provider.DefaultEndpoint)
        {
            EndpointBox.Text = provider.Key == "volcengine-ark" ? provider.DefaultEndpoint : "";
        }

        _isUpdating = false;

        Logger.Debug($"服务商已切换: models={provider.Models.Count} 个可用");
    }

    /// <summary>更新模型下拉列表</summary>
    private void UpdateModelList(string? selectedModel)
    {
        if (ProviderCombo.SelectedItem is not ProviderInfo provider) return;

        ModelCombo.ItemsSource = provider.Models;
        ModelCombo.IsEditable = true;

        var modelToSelect = selectedModel ?? _aiClient.Settings.Model;

        for (int i = 0; i < provider.Models.Count; i++)
        {
            if (provider.Models[i].Id == modelToSelect)
            {
                ModelCombo.SelectedIndex = i;
                Logger.Debug($"模型已选中: {provider.Models[i].Summary}");
                return;
            }
        }

        // 自定义模型名
        ModelCombo.Text = modelToSelect;
        Logger.Debug($"自定义模型名: {modelToSelect}");
    }

    /// <summary>模型选择变更时更新模型输入框的文本</summary>
    private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelCombo.SelectedItem is ModelInfo mi)
        {
            ModelCombo.Text = mi.Id;
            Logger.Debug($"模型选择: {mi.Id}");
        }
    }

    /// <summary>收集表单中的设置</summary>
    private AiSettings CollectSettings()
    {
        var provider = ProviderCombo.SelectedItem as ProviderInfo;
        var model = ModelCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(model) && provider != null && provider.Models.Count > 0)
            model = provider.Models[0].Id;

        return new AiSettings
        {
            Id = _aiClient.Settings.Id,
            DisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text) ? model : DisplayNameBox.Text.Trim(),
            ProviderKey = provider?.Key ?? "openai",
            ApiKey = ApiKeyBox.Password,
            Model = model,
            Endpoint = EndpointBox.Text.Trim(),
        };
    }

    private static bool ValidateSettings(AiSettings settings, out string error)
    {
        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            error = "请填写模型 ID。";
            return false;
        }

        if (settings.ProviderKey is "custom" or "volcengine-ark")
        {
            if (string.IsNullOrWhiteSpace(settings.Endpoint))
            {
                error = settings.ProviderKey == "volcengine-ark"
                    ? "火山方舟 Coding Plan 需要填写 OpenAI Base URL：https://ark.cn-beijing.volces.com/api/coding/v3"
                    : "自定义服务商需要填写 API 端点。";
                return false;
            }

            if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                error = "API 端点必须是 http:// 或 https:// 开头的完整地址。";
                return false;
            }
        }

        if (IsArkEndpoint(settings.Endpoint) && !IsArkCodingPlanEndpoint(settings.Endpoint))
        {
            error = "火山方舟 Coding Plan 的 OpenAI Base URL 应填写：https://ark.cn-beijing.volces.com/api/coding/v3。请勿填写 /api/v3（通用方舟，会产生额外费用）。";
            return false;
        }

        error = "";
        return true;
    }

    private static bool LooksLikeArkSettings(AiSettings settings)
        => settings.ProviderKey == "anthropic" && IsArkEndpoint(settings.Endpoint);

    private static bool IsArkEndpoint(string endpoint)
        => endpoint.Contains("ark.cn-beijing.volces.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsArkCodingPlanEndpoint(string endpoint)
    {
        var normalized = endpoint.TrimEnd('/');
        return normalized.EndsWith("/api/coding/v3", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/api/coding/v3/chat/completions", StringComparison.OrdinalIgnoreCase);
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("测试连接按钮点击");

        var settings = CollectSettings();
        if (!ValidateSettings(settings, out var error))
        {
            TestResultText.Text = $"❌ {error}";
            TestResultText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            TestResultText.Visibility = Visibility.Visible;
            return;
        }

        TestBtn.IsEnabled = false;
        TestResultText.Text = "⏳ 测试连接中…";
        TestResultText.Foreground = System.Windows.Media.Brushes.Gray;
        TestResultText.Visibility = Visibility.Visible;

        try
        {
            _aiClient.Settings = settings;
            var ok = await _aiClient.TestConnectionAsync();

            if (ok)
            {
                TestResultText.Text = "✅ 连接成功！API Key 有效";
                TestResultText.Foreground = System.Windows.Media.Brushes.LimeGreen;
                Logger.Info("连接测试: ✅ 成功");
            }
            else
            {
                TestResultText.Text = "❌ 连接失败，请检查 API Key、模型名和端点";
                TestResultText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                Logger.Warning("连接测试: ❌ 失败");
            }
        }
        catch (Exception ex)
        {
            TestResultText.Text = $"❌ 错误：{ex.Message}";
            TestResultText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            Logger.Exception("连接测试异常", ex);
        }
        finally
        {
            TestBtn.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = CollectSettings();
        Logger.Info($"保存 AI 设置: provider={settings.ProviderKey}, model={settings.Model}, endpoint={settings.Endpoint}");

        if (!ValidateSettings(settings, out var error))
        {
            MessageBox.Show(error, "AI 设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _aiClient.Settings = settings;
        _saved = true;
        SavedIndicator.Text = "✅ 已保存";
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Logger.Debug("取消 AI 设置");
        DialogResult = false;
        Close();
    }

    public bool IsSaved => _saved;
}
