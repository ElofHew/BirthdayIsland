using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using BirthdayIsland.ViewModels;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using FluentAvalonia.UI.Controls;

namespace BirthdayIsland.Views.SettingsPages;

[SettingsPageInfo(
    "birthdayisland.settings",
    "生日显示插件",
    "🎂", // 选中/未选中都用蛋糕 emoji；这两个参数是字符串、会被当作图标文字显示，传图标名会原样显示成字母
    "🎂",
    SettingsPageCategory.External
)]
public partial class BirthdaySettingsPage : SettingsPageBase
{
    private readonly BirthdaySettingsPageViewModel _viewModel;

    public BirthdaySettingsPage(BirthdaySettingsPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        // 注入文件选择器与确认对话框（需要 TopLevel，故在加载后再注入）。
        _viewModel.PickFilesAsync = PickFilesAsync;
        _viewModel.ConfirmAsync = ConfirmAsync;

        // 数据订阅跟页面的显隐成对：VM 是 Transient，挂在构造函数里会泄漏，
        // 见 BirthdaySettingsPageViewModel.Attach 的注释。
        Loaded += (_, _) => _viewModel.Attach();
        Unloaded += (_, _) => _viewModel.Detach();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task<IReadOnlyList<IStorageFile>> PickFilesAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return new List<IStorageFile>();

        return await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择生日表格文件",
            AllowMultiple = false,
            // 只放行 xlsx 与 csv：老的 .xls 是二进制格式，ClosedXML 打不开，
            // 放进筛选器只会让人选中之后看到「文件格式错误」。
            FileTypeFilter = new[]
            {
                new FilePickerFileType("表格文件（.xlsx / .csv）")
                {
                    Patterns = new[] { "*.xlsx", "*.csv" }
                }
            }
        });
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}