using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BirthdayIsland.Models;

/// <summary>
/// 定时提醒由谁驱动。
/// </summary>
public enum ReminderControlSource
{
    /// <summary>由插件自己的定时器在设定时刻触发（默认）。</summary>
    PluginSettings,

    /// <summary>
    /// 插件不再按时间自动触发，改由 ClassIsland 自动化编排。
    /// </summary>
    /// <remarks>
    /// 两套东西都能发提醒，同时开就会在同一分钟各发一条。这个开关的作用就是把其中一套关掉，
    /// 而不是让代码去猜用户想用哪套。
    /// </remarks>
    Automation
}

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
    /// <summary>
    /// 提醒由谁驱动。默认遵循插件设置。
    /// </summary>
    [ObservableProperty]
    private ReminderControlSource _controlSource = ReminderControlSource.PluginSettings;

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

    /// <summary>
    /// 生日歌文件路径。
    /// </summary>
    /// <remarks>
    /// 留空表示用插件包里自带的那首（打包在 .cipx 根目录的 BirthdaySong.mp3）。
    /// 不把某个绝对路径写成默认值：换台机器就失效，而且插件包本来就能自带一份。
    /// </remarks>
    [ObservableProperty]
    private string _songPath = string.Empty;

    /// <summary>生日歌播放秒数；0 表示完整播放。</summary>
    [ObservableProperty]
    private int _songDurationSeconds = 30;

    /// <summary>
    /// 上一次实际发出提醒的日期。存盘是为了防止「刚发完提醒就重启应用」导致同一时间窗口内再次提醒。
    /// </summary>
    [ObservableProperty]
    private DateTime? _lastFiredDate;
}
