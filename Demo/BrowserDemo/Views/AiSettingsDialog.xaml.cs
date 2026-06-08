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

        // 选中当前服务商
        var providers = ProviderManager.GetAll();
        for (int i = 0; i < providers.Count; i++)
        {
            if (providers[i].Key == s.ProviderKey)
            {
                ProviderCombo.SelectedIndex = i;
                break;
            }
        }

        if (ProviderCombo.SelectedIndex < 0 && providers.Count > 0)
            ProviderCombo.SelectedIndex = 0;

        DisplayNameBox.Text = s.DisplayName;
        ApiKeyBox.Password = s.ApiKey;
        EndpointBox.Text = s.Endpoint;

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
            EndpointBox.Text = "";
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

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("测试连接按钮点击");

        TestBtn.IsEnabled = false;
        TestResultText.Text = "⏳ 测试连接中…";
        TestResultText.Foreground = System.Windows.Media.Brushes.Gray;
        TestResultText.Visibility = Visibility.Visible;

        try
        {
            _aiClient.Settings = CollectSettings();
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
