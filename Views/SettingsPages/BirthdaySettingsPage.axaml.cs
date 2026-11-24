using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BirthdayIsland.Models;
using BirthdayIsland.Services;
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

        // 注入文件选择器与对话框（需要 TopLevel，故在加载后再注入）。
        _viewModel.PickFilesAsync = PickFilesAsync;
        _viewModel.ConfirmAsync = ConfirmAsync;
        _viewModel.PickSongFileAsync = PickSongFileAsync;
        _viewModel.PickExportPathAsync = PickExportPathAsync;
        _viewModel.ShowEditDialogAsync = ShowEditDialogAsync;

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

    private async Task<string?> PickSongFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return null;

        // 宿主音频走 MiniAudio，这几类容器它都能解；mp3 是默认值。
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择生日歌",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("音频文件")
                {
                    Patterns = new[] { "*.mp3", "*.wav", "*.flac", "*.ogg", "*.m4a" }
                }
            }
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickExportPathAsync(string suggestedName)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出为 CSV",
            SuggestedFileName = suggestedName,
            DefaultExtension = "csv",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV 文件")
                {
                    Patterns = new[] { "*.csv" }
                }
            }
        });

        return file?.TryGetLocalPath();
    }

    /// <summary>
    /// 单人编辑对话框。返回编辑后的新对象，取消则返回 null。
    /// </summary>
    /// <remarks>
    /// 这里刻意不就地改原对象：<see cref="BirthdayPerson"/> 没有变更通知，就地改界面不会刷新；
    /// 返回新对象交给 <c>BirthdayDataService.ReplacePerson</c> 走集合的替换语义。
    /// 生日用文本框而不是日期选择器，是为了让「只有月日」也能原样保留（日期控件会逼出一个年份）。
    /// </remarks>
    private async Task<BirthdayPerson?> ShowEditDialogAsync(BirthdayPerson person)
    {
        var nameBox = new TextBox { Text = person.Name, Watermark = "姓名" };
        var birthdayBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(person.RawBirthday)
                ? person.Birthday.ToString("yyyy-MM-dd")
                : person.RawBirthday,
            Watermark = "2008-03-27 / 3月27日 / 03-27"
        };
        var classBox = new TextBox { Text = person.ClassName ?? string.Empty, Watermark = "班级（可选）" };
        var remarkBox = new TextBox { Text = person.Remark ?? string.Empty, Watermark = "备注（可选）" };
        var error = new TextBlock
        {
            Foreground = Brushes.OrangeRed,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        var panel = new StackPanel { Spacing = 6, Width = 380 };
        panel.Children.Add(Label("姓名"));
        panel.Children.Add(nameBox);
        panel.Children.Add(Label("生日"));
        panel.Children.Add(birthdayBox);
        panel.Children.Add(Label("班级"));
        panel.Children.Add(classBox);
        panel.Children.Add(Label("备注"));
        panel.Children.Add(remarkBox);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = "编辑生日信息",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        // 校验放在 Closing 里：不合法就取消关闭，把错误提示留在对话框内，
        // 而不是关掉之后再弹一条无法回改的提示。
        // 注意 lambda 的第一个参数不能写成 _：那样它就是名为 _ 的实参，
        // 后面 TryParseBirthdayText(..., out _) 的弃元会被解析成它，编译期直接报类型不匹配。
        dialog.Closing += (sender, args) =>
        {
            if (args.Result != ContentDialogResult.Primary)
                return;

            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                error.Text = "姓名不能为空。";
                error.IsVisible = true;
                args.Cancel = true;
                return;
            }

            if (!ExcelImportService.TryParseBirthdayText(birthdayBox.Text ?? string.Empty, out _))
            {
                error.Text = "生日无法识别。支持 2008-03-27、2008/03/27、03-27、3月27日 等写法。";
                error.IsVisible = true;
                args.Cancel = true;
            }
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return null;

        ExcelImportService.TryParseBirthdayText(birthdayBox.Text ?? string.Empty, out var birthday);

        return new BirthdayPerson
        {
            Name = nameBox.Text!.Trim(),
            RawBirthday = (birthdayBox.Text ?? string.Empty).Trim(),
            Birthday = birthday,
            ClassName = TrimOrNull(classBox.Text),
            Remark = TrimOrNull(remarkBox.Text)
        };
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Avalonia.Thickness(0, 6, 0, 0)
    };

    private static string? TrimOrNull(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
