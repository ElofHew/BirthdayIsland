using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BirthdayIsland.Models;

/// <summary>
/// 定时提醒设置。
/// </summary>
/// <remarks>
/// 继承 ObservableObject 是为了让设置页能直接双向绑定，并在改动后立刻落盘
/// （SettingsPageViewModel 订阅它的 PropertyChanged）。JSON 序列化只看公开属性，
/// 事件不会被写进 birthdays.json。
/// </remarks>
public partial class BirthdayReminderConfig : ObservableObject
{
    /// <summary>默认的生日歌位置。可在设置页改成别的文件。</summary>
    public const string DefaultSongPath = @"D:\WinMusic\AE\BirthdaySong.mp3";

    /// <summary>是否启用定时提醒。</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>提醒时刻（时）。</summary>
    [ObservableProperty]
    private int _hour = 8;

    /// <summary>提醒时刻（分）。</summary>
    [ObservableProperty]
    private int _minute;

    /// <summary>正文（漂浮在界面上的那条）显示秒数，可选 3 / 5 / 10。</summary>
    [ObservableProperty]
    private int _notificationDurationSeconds = 5;

    /// <summary>遮罩层显示秒数，可选 1 / 2 / 3。</summary>
    [ObservableProperty]
    private int _maskDurationSeconds = 2;

    /// <summary>是否在提醒的同时播放生日歌。</summary>
    [ObservableProperty]
    private bool _isSongEnabled;

    /// <summary>生日歌文件路径。</summary>
    [ObservableProperty]
    private string _songPath = DefaultSongPath;

    /// <summary>生日歌播放秒数；0 表示完整播放。</summary>
    [ObservableProperty]
    private int _songDurationSeconds = 30;

    /// <summary>
    /// 上一次实际发出提醒的日期。存盘是为了防止「刚发完提醒就重启应用」导致同一时间窗口内再次提醒。
    /// </summary>
    [ObservableProperty]
    private DateTime? _lastFiredDate;
}
