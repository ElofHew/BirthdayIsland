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
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    /// <summary>
    /// 距离上一次真正发出提醒过去了多久，用于挡住自动化套娃。
    /// </summary>
    /// <remarks>
    /// 2 秒是个折中：递归套娃是毫秒级，一定被挡住；
    /// 而用户手动点两次、或两个工作流相隔一会儿各发一次，仍然照发不误
    /// （自动化优先级高于插件设置，不能因为「今天发过」就不执行）。
    /// </remarks>
    private static readonly TimeSpan RepeatGuardWindow = TimeSpan.FromSeconds(2);

    private readonly object _announceLock = new();
    private DateTime _lastAnnounceAt = DateTime.MinValue;

    /// <summary>发出一次生日提醒后触发，供自动化触发器使用。</summary>
    public event EventHandler<BirthdayAnnouncementEventArgs>? Announced;

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

        // 「由自动化驱动」时插件完全不参与计时：两条路都开着的话，同一分钟会各发一条，
        // 与其在代码里猜用户想要哪条，不如让用户显式选一套。
        if (reminder.ControlSource != ReminderControlSource.PluginSettings)
            return;

        if (!reminder.IsEnabled)
            return;

        var now = _exactTimeService.GetCurrentLocalDateTime();
        if (!ReminderSchedule.ShouldFire(now, reminder.Hour, reminder.Minute, reminder.LastFiredDate))
            return;

        var names = _dataService.GetTodayBirthdays(now).Select(p => p.Name).ToList();

        // 没人过生日就不记账。记账是为了防重复，而「今天没人过生日」之后随时可能变成「有」——
        // 用户先开提醒再导入名单时，若这里已经记了「今天已提醒」，当天的提醒就永远发不出来了。
        // 窗口内每 20 秒重算一次的成本可以忽略（名单就几百条）。
        if (names.Count == 0)
            return;

        // 先记账再发送：万一后面发提醒或播歌抛异常，也不会在下一个 tick 里重复触发。
        _dataService.MarkReminderFired(now);

        Announce(names, reminder);
    }

    /// <summary>
    /// 立即发出一次生日提醒（自动化行动或手动触发用）。
    /// </summary>
    /// <remarks>
    /// 和定时提醒的区别只有一点：这里不看「今天是否已经发过」，也不看 <c>ControlSource</c> ——
    /// 自动化是用户显式编排的，插件设置不该把它拦下来（自动化优先级高于插件自带计时）。
    /// 但发完仍然会记账，于是插件自带的计时当天就不会再补一条。
    /// </remarks>
    public void AnnounceNow()
    {
        // 自动化行动可能在后台线程跑，而 ShowNotification 和 MarkReminderFired 都会碰
        // UI 绑定的集合（提醒队列、ObservableCollection），统一回 UI 线程再做。
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(AnnounceNow);
            return;
        }

        var now = _exactTimeService.GetCurrentLocalDateTime();
        var reminder = _dataService.Config.Reminder;
        var names = _dataService.GetTodayBirthdays(now).Select(p => p.Name).ToList();

        // 同上：没人过生日就不占用今天的额度。
        if (names.Count == 0)
        {
            _logger.LogInformation("今天没人过生日，跳过这次手动提醒");
            return;
        }

        _dataService.MarkReminderFired(now);

        Announce(names, reminder);
    }

    /// <summary>
    /// 取当前正在运行的提醒提供方实例。
    /// </summary>
    /// <remarks>
    /// 本类型只以 <c>IHostedService</c> 的形式注册（<c>AddNotificationProvider</c> 内部就是这样加的），
    /// 容器里没有「具体类型」这条注册，所以只能从宿主的托管服务列表里按类型捞。
    /// 宿主自己的 NotificationAction 也是这么拿提供方实例的。
    /// 这样取还能保证拿到的是同一个对象 —— 再注册一次单例就会多出一个实例，
    /// 于是又多一个定时器、多一份提醒。
    /// </remarks>
    public static BirthdayNotificationProvider? TryGetCurrent() =>
        IAppHost.Host?.Services.GetServices<IHostedService>()
            .OfType<BirthdayNotificationProvider>()
            .FirstOrDefault();

    private void Announce(IReadOnlyList<string> names, BirthdayReminderConfig reminder)
    {
        if (!TryEnterAnnounce())
        {
            // 走到这里基本只有一种情况：工作流把「生日提醒触发时」和「发送生日提醒」
            // 串在了一起，Announce → 触发器 → 行动 → AnnounceNow → Announce …
            // 宿主那边工作流是异步跑的，所以用「上一次提醒的时间」而不是重入标志位来挡，
            // 异步套娃同样能在第二层停住。
            _logger.LogWarning("两秒内已经发过一次生日提醒，忽略这次重复触发（多半是自动化里触发器与行动互相套娃）");
            return;
        }

        ShowBirthdayNotification(names, reminder);

        // 先抛事件再播歌：自动化里挂在「生日提醒触发时」的工作流能立刻跑起来，
        // 不必等音频结束。
        Announced?.Invoke(this, new BirthdayAnnouncementEventArgs(names));

        if (reminder.IsSongEnabled)
            _songPlayer.Play(reminder.SongPath, reminder.SongDurationSeconds);
    }

    /// <summary>
    /// 过了重复保护窗口才允许这一次提醒，返回 false 表示应忽略。
    /// </summary>
    private bool TryEnterAnnounce()
    {
        lock (_announceLock)
        {
            var now = DateTime.UtcNow;
            if (now - _lastAnnounceAt < RepeatGuardWindow)
                return false;

            _lastAnnounceAt = now;
            return true;
        }
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
