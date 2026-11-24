using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using BirthdayIsland.Models;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using Microsoft.Extensions.Logging;

namespace BirthdayIsland.Services;

/// <summary>
/// 生日提醒的提醒提供方，同时负责「到点触发」的调度。
/// </summary>
/// <remarks>
/// <para>
/// <c>INotificationHostService.ShowNotification</c> 是 internal 的，插件够不着；
/// 注册成 <see cref="NotificationProviderBase"/> 之后才有公开的 <c>ShowNotification</c>。
/// 注册进来的副作用是这个提供方会出现在「设置 → 提醒」里，用户能单独给它调音效/特效。
/// </para>
/// <para>
/// 调度没有用「主界面计时器」（<c>ILessonsService.PostMainTimerTicked</c>），
/// 因为那个计时器是否在走取决于课表状态；这里用一个独立的 20 秒定时器，
/// 只在到点时把活丢给 UI 线程，和课表无关。
/// </para>
/// </remarks>
[NotificationProviderInfo("D3F1A6E8-7C42-4B9A-8E5D-1A2C3B4D5E6F", "生日提醒", "\uE787",
    "在设定的时间提醒今天是哪些同学的生日，可选同时播放生日歌。")]
public class BirthdayNotificationProvider : NotificationProviderBase
{
    /// <summary>检查间隔。20 秒足够覆盖 5 分钟的补发窗口，又不会一直在算。</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(20);

    /// <summary>首次检查的延迟，等宿主把界面和音频设备都初始化完。</summary>
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(5);

    private readonly BirthdayDataService _dataService;
    private readonly BirthdaySongPlayer _songPlayer;
    private readonly IExactTimeService _exactTimeService;
    private readonly ILogger<BirthdayNotificationProvider> _logger;
    private readonly Timer _timer;

    /// <summary>0 = 空闲，1 = 已有一个检查在 UI 线程排队/执行中。</summary>
    private int _checkInFlight;

    public BirthdayNotificationProvider(
        BirthdayDataService dataService,
        BirthdaySongPlayer songPlayer,
        IExactTimeService exactTimeService,
        ILogger<BirthdayNotificationProvider> logger)
    {
        _dataService = dataService;
        _songPlayer = songPlayer;
        _exactTimeService = exactTimeService;
        _logger = logger;

        // 基类的 StartAsync 不是 virtual，override 不了，所以在构造时就把定时器挂上。
        // 宿主启动时会把本类型作为 IHostedService 解析出来，构造一定发生在应用启动阶段。
        _timer = new Timer(_ => Tick(), null, FirstCheckDelay, CheckInterval);
    }

    private void Tick()
    {
        // 上一轮还没跑完（比如 UI 线程正忙）就跳过这一轮，不要堆积。
        if (Interlocked.Exchange(ref _checkInFlight, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                CheckAndNotify();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查生日提醒时出错");
            }
            finally
            {
                Interlocked.Exchange(ref _checkInFlight, 0);
            }
        });
    }

    private void CheckAndNotify()
    {
        var reminder = _dataService.Config.Reminder;
        if (!reminder.IsEnabled)
            return;

        var now = _exactTimeService.GetCurrentLocalDateTime();
        if (!ReminderSchedule.ShouldFire(now, reminder.Hour, reminder.Minute, reminder.LastFiredDate))
            return;

        var names = _dataService.GetTodayBirthdays(now).Select(p => p.Name).ToList();

        // 先记账再发送：万一后面发提醒或播歌抛异常，也不会在下一个 tick 里重复触发。
        _dataService.MarkReminderFired(now);

        if (names.Count == 0)
            return; // 今天没人过生日，静默跳过

        ShowBirthdayNotification(names, reminder);

        if (reminder.IsSongEnabled)
            _songPlayer.Play(reminder.SongPath, reminder.SongDurationSeconds);
    }

    private void ShowBirthdayNotification(IReadOnlyList<string> names, BirthdayReminderConfig reminder)
    {
        var greeting = BuildGreeting(names);

        var request = new NotificationRequest
        {
            // 遮罩层：全屏一闪而过的那层，时长单独配（1/2/3 秒）
            MaskContent = NotificationContent.CreateTwoIconsMask("生日快乐", factory: x =>
            {
                x.Duration = TimeSpan.FromSeconds(Math.Max(1, reminder.MaskDurationSeconds));
                x.SpeechContent = greeting;
                x.IsSpeechEnabled = false;
            }),
            // 正文：浮在桌面上的那条，时长 3/5/10 秒
            OverlayContent = NotificationContent.CreateSimpleTextContent(greeting, x =>
            {
                x.Duration = TimeSpan.FromSeconds(Math.Max(1, reminder.NotificationDurationSeconds));
                x.SpeechContent = greeting;
                x.IsSpeechEnabled = false;
            })
        };

        ShowNotification(request);
        _logger.LogInformation("已发出生日提醒：{Greeting}", greeting);
    }

    /// <summary>
    /// 单人：「张三，祝你生日快乐！」；多人：「李四、王五，祝你们生日快乐！」
    /// </summary>
    public static string BuildGreeting(IReadOnlyList<string> names)
    {
        return names.Count switch
        {
            0 => string.Empty,
            1 => $"{names[0]}，祝你生日快乐！",
            _ => $"{string.Join("、", names)}，祝你们生日快乐！"
        };
    }
}
