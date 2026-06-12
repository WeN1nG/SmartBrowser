using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using BrowserDemo.Models;
using BrowserDemo.Services;
using BrowserDemo.ViewModels;

namespace BrowserDemo.Views;

public partial class AiModelSelectionDialog : Window
{
    private readonly ChatViewModel _chatVm;

    public ObservableCollection<ModelProfileRow> Rows { get; } = new();

    public AiModelSelectionDialog(ChatViewModel chatVm, Window owner)
    {
        InitializeComponent();
        _chatVm = chatVm;
        Owner = owner;
        DataContext = this;
        LoadRows();
    }

    private void LoadRows()
    {
        using var _ = Logger.Trace("AiModelSelectionDialog::LoadRows");
        Rows.Clear();
        var store = AiSettingsStore.Load();
        if (store.Profiles.Count == 0)
        {
            var settings = new AiSettings();
            store.Profiles.Add(settings);
            store.ActiveId = settings.Id;
        }

        foreach (var profile in store.Profiles)
        {
            Rows.Add(new ModelProfileRow(profile)
            {
                IsActive = profile.Id == store.ActiveId,
                IsDefault = profile.Id == store.DefaultId
            });
        }

        if (!Rows.Any(x => x.IsActive) && Rows.Count > 0)
            Rows[0].IsActive = true;

        Logger.Debug($"AI 模型配置加载完成: {Rows.Count} 个配置");
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var client = new AiClient { Settings = new AiSettings { DisplayName = "新模型" } };
        var dialog = new AiSettingsDialog(client, this);
        if (dialog.ShowDialog() == true && dialog.IsSaved)
        {
            Rows.Add(new ModelProfileRow(Clone(client.Settings)) { IsActive = Rows.Count == 0 });
            StatusText.Text = "已添加模型，点击保存后生效";
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ModelProfileRow row) return;

        var client = new AiClient { Settings = Clone(row.Settings) };
        var dialog = new AiSettingsDialog(client, this);
        if (dialog.ShowDialog() == true && dialog.IsSaved)
        {
            row.Settings = Clone(client.Settings);
            StatusText.Text = "已更新模型，点击保存后生效";
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ModelProfileRow row) return;
        if (Rows.Count <= 1)
        {
            MessageBox.Show("至少需要保留一个模型配置。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"确定删除模型“{row.Settings.DisplayName}”？", "删除模型",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var wasActive = row.IsActive;
        var wasDefault = row.IsDefault;
        Rows.Remove(row);
        if (wasActive && Rows.Count > 0) Rows[0].IsActive = true;
        if (wasDefault && Rows.Count > 0) Rows[0].IsDefault = true;
    }

    private void ActiveRadio_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModelProfileRow selected) return;
        foreach (var row in Rows)
            row.IsActive = ReferenceEquals(row, selected);
    }

    private void DefaultRadio_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModelProfileRow selected) return;
        foreach (var row in Rows)
            row.IsDefault = ReferenceEquals(row, selected);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        using var _ = Logger.Trace("AiModelSelectionDialog::Save_Click");
        if (Rows.Count == 0) return;
        if (!Rows.Any(x => x.IsActive)) Rows[0].IsActive = true;

        var store = new AiSettingsStore
        {
            Profiles = Rows.Select(x => x.Settings).ToList(),
            ActiveId = Rows.FirstOrDefault(x => x.IsActive)?.Settings.Id,
            DefaultId = Rows.FirstOrDefault(x => x.IsDefault)?.Settings.Id
        };
        store.Save();
        var active = store.ResolveActive();
        Logger.Info($"AI 模型配置保存完成: active provider={active.ProviderKey}, model={active.Model}");
        _chatVm.ApplySettings(active);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static AiSettings Clone(AiSettings source) => new()
    {
        Id = source.Id,
        DisplayName = source.DisplayName,
        ProviderKey = source.ProviderKey,
        ApiKey = source.ApiKey,
        Model = source.Model,
        Endpoint = source.Endpoint
    };
}

public class ModelProfileRow : INotifyPropertyChanged
{
    private AiSettings _settings;
    private bool _isActive;
    private bool _isDefault;

    public ModelProfileRow(AiSettings settings)
    {
        _settings = settings;
    }

    public AiSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public bool IsDefault
    {
        get => _isDefault;
        set { _isDefault = value; OnPropertyChanged(); }
    }

    public string Summary => $"{Settings.ProviderKey} / {Settings.Model}";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
