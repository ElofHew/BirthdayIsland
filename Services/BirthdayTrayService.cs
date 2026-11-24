using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BirthdayIsland.Services;

/// <summary>
/// 在宿主托盘菜单的「更多选项」里放一条「播放生日歌 / 停止播放」。
/// </summary>
/// <remarks>
/// <para>
/// 生日歌是在后台响的，播起来之后原来没有任何入口能停 —— 提醒只显示几秒，歌可能放一分钟。
/// 托盘菜单是应用最小化后唯一还够得着的地方，所以在这里留一条。
/// </para>
/// <para>
/// 时序是这里唯一的难点，两个坑都踩过：
/// </para>
/// <list type="number">
/// <item>
/// 宿主的 <c>MainWindow</c> 是在 <c>Host.StartAsync()</c> <b>之后</b>才构造的，而
/// <c>TaskBarIconService.MoreOptionsMenu</c> 是在 MainWindow 的构造函数里才被赋值的。
/// 在 <c>StartAsync</c> 里同步去加，<c>MoreOptionsMenuItems</c> 直接空引用。
/// </item>
/// <item>
/// 重试用的 <see cref="DispatcherTimer"/> 必须在 UI 线程上创建。在托管服务的启动线程
/// （不一定是 UI 线程）上 new 一个，它绑到的是那个线程的 dispatcher，根本不会 tick ——
/// 表现就是「只试了一次，之后再也没试过，菜单里什么都没有」。
/// </item>
/// </list>
/// <para>
/// 所以：先 Post 回 UI 线程（Background 优先级，等启动流程走完），在那里失败就用
/// UI 线程上的计时器每秒重试，最多一分钟。
/// </para>
/// </remarks>
public class BirthdayTrayService : IHostedService
{
    private const int MaxAttempts = 60;

    private readonly BirthdaySongPlayer _songPlayer;
    private readonly ILogger<BirthdayTrayService> _logger;

    private NativeMenuItem? _item;
    private DispatcherTimer? _retryTimer;

    public BirthdayTrayService(BirthdaySongPlayer songPlayer, ILogger<BirthdayTrayService> logger)
    {
        _songPlayer = songPlayer;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _songPlayer.PlaybackStateChanged += OnPlaybackStateChanged;

        // 必须回 UI 线程：既为了等 MainWindow 建好，也为了后面创建的 DispatcherTimer 能跑。
        Dispatcher.UIThread.Post(() => TryAttach(0), DispatcherPriority.Background);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _songPlayer.PlaybackStateChanged -= OnPlaybackStateChanged;

        _retryTimer?.Stop();
        _retryTimer = null;

        if (_item != null)
        {
            try
            {
                IAppHost.Host?.Services.GetService<ITaskBarIconService>()
                    ?.MoreOptionsMenuItems.Remove(_item);
            }
            catch (Exception)
            {
                // 宿主正在关闭，菜单没了也无所谓。
            }

            _item = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 尝试挂菜单项，失败就一秒后再来。<b>只在 UI 线程调用。</b>
    /// </summary>
    private void TryAttach(int attempt)
    {
        if (TryAddItem())
            return;

        if (attempt >= MaxAttempts)
        {
            _logger.LogWarning("试了 {Attempts} 次仍然没能把生日歌加进托盘菜单，放弃（宿主的托盘菜单可能不可用）",
                MaxAttempts);
            return;
        }

        _retryTimer?.Stop();
        _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _retryTimer.Tick += (_, _) =>
        {
            _retryTimer?.Stop();
            TryAttach(attempt + 1);
        };
        _retryTimer.Start();
    }

    private bool TryAddItem()
    {
        var service = IAppHost.Host?.Services.GetService<ITaskBarIconService>();
        if (service == null)
            return false; // 宿主还没把托盘服务放进容器

        if (_item == null)
        {
            _item = new NativeMenuItem();
            _item.Click += (_, _) => _songPlayer.Toggle();
        }

        // 先设标题再加进菜单：原生菜单有可能在 Add 时就把条目快照下去，
        // 之后再改 Header 不一定刷得到。
        RefreshHeader();

        try
        {
            // MoreOptionsMenuItems 背后是 MoreOptionsMenu!.Items，菜单没挂上时会抛空引用。
            if (!service.MoreOptionsMenuItems.Contains(_item))
                service.MoreOptionsMenuItems.Add(_item);

            _logger.LogDebug("已把「播放生日歌」加入托盘菜单的更多选项");
            return true;
        }
        catch (Exception)
        {
            // 菜单还没建好，交给上层重试。
            return false;
        }
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        // 播放自然结束时事件来自后台线程，改菜单标题必须回 UI 线程。
        Dispatcher.UIThread.Post(RefreshHeader);
    }

    private void RefreshHeader()
    {
        if (_item == null)
            return;

        _item.Header = _songPlayer.IsPlaying ? "停止播放" : "播放生日歌";
    }
}
