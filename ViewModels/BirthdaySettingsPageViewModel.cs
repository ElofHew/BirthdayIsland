using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    private readonly BirthdayDataService _dataService;
    private readonly ExcelImportService _excelImportService;

    /// <summary>由设置页在加载后注入，用于弹出系统文件选择器。</summary>
    public Func<Task<IReadOnlyList<IStorageFile>>>? PickFilesAsync { get; set; }

    /// <summary>由设置页在加载后注入，用于弹出确认对话框。返回 true 表示确认。</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public ObservableCollection<BirthdayPerson> People => _dataService.People;

    public ObservableCollection<BirthdayPerson> TodayBirthdays { get; } = new();

    public int TotalCount => People.Count;

    public int TodayBirthdayCount => TodayBirthdays.Count;

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
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _importErrors = new();

    /// <summary>「导入失败明细」那一块的可见性。XAML 里不能直接拿 Count(int) 当 bool 用。</summary>
    public bool HasImportErrors => ImportErrors.Count > 0;

    partial void OnImportErrorsChanged(ObservableCollection<string> value) =>
        OnPropertyChanged(nameof(HasImportErrors));

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
        Refresh();
    }

    public void Detach() => _dataService.DataChanged -= OnDataChanged;

    private void OnDataChanged(object? sender, EventArgs e) => Refresh();

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

        // 配置可能被别处改过，选中项跟着配置走一遍。
        SelectedLeapDayOption =
            LeapDayOptions.FirstOrDefault(o => o.Value == _dataService.Config.LeapDayDisplayMode)
            ?? LeapDayOptions[0];

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TodayBirthdayCount));
        OnPropertyChanged(nameof(LastImportFileName));
        OnPropertyChanged(nameof(LastImportTimeText));
    }
}
