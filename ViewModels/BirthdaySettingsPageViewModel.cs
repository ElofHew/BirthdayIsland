using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using BirthdayIsland.Models;
using BirthdayIsland.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BirthdayIsland.ViewModels;

/// <summary>闰日显示规则的一个可选项，供设置页下拉框直接绑定。</summary>
public sealed record LeapDayOption(LeapDayDisplayMode Value, string Label);

public partial class BirthdaySettingsPageViewModel : ObservableObject
{
    /// <summary>生日歌播放时长下拉框的取值，和 <see cref="SongDurationLabels"/> 一一对应；0 表示完整播放。</summary>
    private static readonly int[] SongDurationValues = { 30, 45, 60, 75, 0 };

    private readonly BirthdayDataService _dataService;
    private readonly ExcelImportService _excelImportService;

    /// <summary>由设置页在加载后注入，用于弹出系统文件选择器。</summary>
    public Func<Task<IReadOnlyList<IStorageFile>>>? PickFilesAsync { get; set; }

    /// <summary>由设置页在加载后注入，用于弹出确认对话框。返回 true 表示确认。</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>由设置页在加载后注入，用于选择生日歌文件。返回 null 表示取消。</summary>
    public Func<Task<string?>>? PickSongFileAsync { get; set; }

    /// <summary>由设置页在加载后注入，用于选择导出目标文件。参数是建议文件名，返回 null 表示取消。</summary>
    public Func<string, Task<string?>>? PickExportPathAsync { get; set; }

    /// <summary>由设置页在加载后注入，用于弹出编辑对话框。返回 null 表示取消。</summary>
    public Func<BirthdayPerson, Task<BirthdayPerson?>>? ShowEditDialogAsync { get; set; }

    /// <summary>全部数据（导入、删除直接作用在这上面）。</summary>
    public ObservableCollection<BirthdayPerson> People => _dataService.People;

    /// <summary>按搜索框过滤后的列表，界面绑这个。</summary>
    public ObservableCollection<BirthdayPerson> VisiblePeople { get; } = new();

    public ObservableCollection<BirthdayPerson> TodayBirthdays { get; } = new();

    public BirthdayReminderConfig Reminder => _dataService.Config.Reminder;

    public IReadOnlyList<int> NotificationDurationOptions { get; } = new[] { 3, 5, 10 };

    public IReadOnlyList<int> MaskDurationOptions { get; } = new[] { 1, 2, 3 };

    public IReadOnlyList<string> SongDurationLabels { get; } = new[] { "30 秒", "45 秒", "60 秒", "75 秒", "完整播放" };

    public int TotalCount => People.Count;

    public int TodayBirthdayCount => TodayBirthdays.Count;

    public bool HasTodayBirthdays => TodayBirthdays.Count > 0;

    public bool HasVisiblePeople => VisiblePeople.Count > 0;

    public string VisibleCountText => SearchText.Trim().Length == 0
        ? $"共 {People.Count} 人"
        : $"筛选出 {VisiblePeople.Count} / {People.Count} 人";

    public string ReminderSummary => Reminder.IsEnabled
        ? $"每天 {Reminder.Hour:00}:{Reminder.Minute:00} 提醒"
        : "定时提醒未启用";

    /// <summary>生日歌路径的展示文本。配置为空表示用插件包里自带的那首。</summary>
    public string SongPathDisplay => string.IsNullOrWhiteSpace(Reminder.SongPath)
        ? $"（使用插件自带：{BirthdaySongPlayer.BundledSongFileName}）"
        : Reminder.SongPath;

    public string LastImportFileName => _dataService.LastImportFileName ?? "（尚未导入）";

    public string LastImportTimeText =>
        _dataService.LastImportTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "（尚未导入）";

    /// <summary>闰日规则的三个选项，顺序即下拉框顺序。</summary>
    public IReadOnlyList<LeapDayOption> LeapDayOptions { get; } = new[]
    {
        new LeapDayOption(LeapDayDisplayMode.OnlyOnFeb29, "只在 2 月 29 日显示"),
        new LeapDayOption(LeapDayDisplayMode.ShowOnFeb28WhenNonLeapYear, "非闰年提前到 2 月 28 日"),
        new LeapDayOption(LeapDayDisplayMode.ShowOnMar1WhenNonLeapYear, "非闰年顺延到 3 月 1 日")
    };

    [ObservableProperty]
    private LeapDayOption? _selectedLeapDayOption;

    /// <summary>
    /// 选中项回写到配置。Save() 会触发 DataChanged，而 Refresh() 又按配置重新赋值选中项，
    /// 两边都靠「值没变就 return」收口，否则会来回触发。
    /// </summary>
    partial void OnSelectedLeapDayOptionChanged(LeapDayOption? value)
    {
        if (value == null)
            return;
        if (_dataService.Config.LeapDayDisplayMode == value.Value)
            return;

        _dataService.SetLeapDayDisplayMode(value.Value);
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => RebuildVisiblePeople();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _importErrors = new();

    /// <summary>「导入失败明细」那一块的可见性。XAML 里不能直接拿 Count(int) 当 bool 用。</summary>
    public bool HasImportErrors => ImportErrors.Count > 0;

    partial void OnImportErrorsChanged(ObservableCollection<string> value) =>
        OnPropertyChanged(nameof(HasImportErrors));

    /// <summary>生日歌播放时长：下拉框用下标，配置里存秒数（0 = 完整播放）。</summary>
    public int SelectedSongDurationIndex
    {
        get
        {
            int index = Array.IndexOf(SongDurationValues, Reminder.SongDurationSeconds);
            return index < 0 ? 0 : index;
        }
        set
        {
            if (value < 0 || value >= SongDurationValues.Length)
                return;
            if (Reminder.SongDurationSeconds == SongDurationValues[value])
                return;

            Reminder.SongDurationSeconds = SongDurationValues[value];
            OnPropertyChanged();
        }
    }

    public BirthdaySettingsPageViewModel(
        BirthdayDataService dataService,
        ExcelImportService excelImportService)
    {
        _dataService = dataService;
        _excelImportService = excelImportService;
    }

    /// <summary>
    /// 设置页显示时调用。本 VM 是 Transient，如果在构造函数里订阅单例的 DataChanged，
    /// 每开一次设置页就多挂一个永不释放的处理器，所以订阅/退订跟着页面的 Loaded/Unloaded 走。
    /// 先 - 再 + 是为了让重复触发的 Loaded 只挂一份。
    /// </summary>
    public void Attach()
    {
        _dataService.DataChanged -= OnDataChanged;
        _dataService.DataChanged += OnDataChanged;

        Reminder.PropertyChanged -= OnReminderChanged;
        Reminder.PropertyChanged += OnReminderChanged;

        Refresh();
    }

    public void Detach()
    {
        _dataService.DataChanged -= OnDataChanged;
        Reminder.PropertyChanged -= OnReminderChanged;
    }

    private void OnDataChanged(object? sender, EventArgs e) => Refresh();

    private void OnReminderChanged(object? sender, PropertyChangedEventArgs e)
    {
        // LastFiredDate 是提醒服务内部的记账，MarkReminderFired 自己会存盘，
        // 这里再存一次纯属重复写文件。
        if (e.PropertyName != nameof(BirthdayReminderConfig.LastFiredDate))
            _dataService.Save();

        OnPropertyChanged(nameof(ReminderSummary));
        OnPropertyChanged(nameof(SongPathDisplay));
        OnPropertyChanged(nameof(SelectedSongDurationIndex));
    }

    [RelayCommand]
    private async Task ImportExcelAsync()
    {
        if (PickFilesAsync == null)
        {
            StatusMessage = "无法打开文件选择器。";
            return;
        }

        var files = await PickFilesAsync();
        var file = files?.FirstOrDefault();
        if (file == null)
            return; // 用户取消

        string path = file.TryGetLocalPath() ?? file.Path.LocalPath;

        BirthdayImportResult result;
        try
        {
            result = _excelImportService.Import(path);
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入失败：{ex.Message}";
            return;
        }

        ImportErrors = new ObservableCollection<string>(
            result.Errors.Select(e => $"第 {e.RowNumber} 行：{e.Reason}"));

        if (result.Fatal)
        {
            StatusMessage = result.Message ?? "导入失败。";
            return;
        }

        // 覆盖导入并保存
        _dataService.AddOrReplacePeople(result.People, file.Name);
        StatusMessage = result.Message ?? $"导入完成：成功 {result.SuccessCount} 条，失败 {result.ErrorCount} 条。";
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        bool confirmed = ConfirmAsync == null
            ? true
            : await ConfirmAsync("清空数据", "确定要清空全部生日数据吗？此操作不可撤销。");

        if (!confirmed)
            return;

        _dataService.Clear();
        ImportErrors = new ObservableCollection<string>();
        StatusMessage = "已清空全部数据。";
    }

    [RelayCommand]
    private void DeletePerson(BirthdayPerson? person)
    {
        if (person == null)
            return;

        _dataService.RemovePerson(person);
        StatusMessage = $"已删除：{person.Name}";
    }

    [RelayCommand]
    private async Task EditPersonAsync(BirthdayPerson? person)
    {
        if (person == null || ShowEditDialogAsync == null)
            return;

        var edited = await ShowEditDialogAsync(person);
        if (edited == null)
            return; // 用户取消

        _dataService.ReplacePerson(person, edited);
        StatusMessage = $"已更新：{edited.Name}";
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (PickExportPathAsync == null)
        {
            StatusMessage = "无法打开保存对话框。";
            return;
        }

        if (People.Count == 0)
        {
            StatusMessage = "没有可导出的数据。";
            return;
        }

        string? path = await PickExportPathAsync($"生日名单-{DateTime.Now:yyyyMMdd}.csv");
        if (string.IsNullOrWhiteSpace(path))
            return; // 用户取消

        try
        {
            // 必须 UTF-8 带 BOM：不带 BOM 时 Excel 会按本地 ANSI 打开，中文姓名全变乱码。
            await File.WriteAllTextAsync(path, BirthdayCsvWriter.Build(People), new UTF8Encoding(true));
            StatusMessage = $"已导出 {People.Count} 条到 {Path.GetFileName(path)}。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PickSongAsync()
    {
        if (PickSongFileAsync == null)
            return;

        string? path = await PickSongFileAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;

        Reminder.SongPath = path;
        StatusMessage = $"生日歌已设为：{Path.GetFileName(path)}";
    }

    /// <summary>清空自定义路径，回到插件包里自带的那首。</summary>
    [RelayCommand]
    private void ResetSong()
    {
        if (string.IsNullOrWhiteSpace(Reminder.SongPath))
            return;

        Reminder.SongPath = string.Empty;
        StatusMessage = "已恢复为插件自带的生日歌。";
    }

    [RelayCommand]
    private void Reload()
    {
        _dataService.Load();
        StatusMessage = "已从本地文件重新读取数据。";
    }

    private void Refresh()
    {
        TodayBirthdays.Clear();
        foreach (var person in _dataService.GetTodayBirthdays())
            TodayBirthdays.Add(person);

        RebuildVisiblePeople();

        // 配置可能被别处改过，选中项跟着配置走一遍。
        SelectedLeapDayOption =
            LeapDayOptions.FirstOrDefault(o => o.Value == _dataService.Config.LeapDayDisplayMode)
            ?? LeapDayOptions[0];

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TodayBirthdayCount));
        OnPropertyChanged(nameof(HasTodayBirthdays));
        OnPropertyChanged(nameof(LastImportFileName));
        OnPropertyChanged(nameof(LastImportTimeText));
        OnPropertyChanged(nameof(ReminderSummary));
        OnPropertyChanged(nameof(SongPathDisplay));
        OnPropertyChanged(nameof(SelectedSongDurationIndex));
    }

    private void RebuildVisiblePeople()
    {
        string keyword = SearchText.Trim();

        VisiblePeople.Clear();
        foreach (var person in People
                     .Where(p => Matches(p, keyword))
                     .OrderBy(p => p.Birthday.Month)
                     .ThenBy(p => p.Birthday.Day)
                     .ThenBy(p => p.Name, StringComparer.CurrentCulture))
        {
            VisiblePeople.Add(person);
        }

        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(HasVisiblePeople));
    }

    private static bool Matches(BirthdayPerson person, string keyword)
    {
        if (keyword.Length == 0)
            return true;

        return Contains(person.Name, keyword)
               || Contains(person.ClassName, keyword)
               || Contains(person.Remark, keyword);

        static bool Contains(string? value, string keyword) =>
            value != null && value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
    }
}
