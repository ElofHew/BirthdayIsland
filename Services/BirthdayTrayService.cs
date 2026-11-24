using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

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
/// <see cref="ITaskBarIconService.MoreOptionsMenuItems"/> 背后是 <c>MoreOptionsMenu!.Items</c>，
/// 而那个菜单要等宿主 MainWindow 构造完才存在，取早了直接空引用。
/// 插件有可能比它先起来，所以加不上就连隔一秒重试，最多半分钟。
/// </para>
/// </remarks>
public class BirthdayTrayService : IHostedService
{
    private readonly BirthdaySongPlayer _songPlayer;
    private readonly ILogger<BirthdayTrayService> _logger;

    private NativeMenuItem? _item;
    private DispatcherTimer? _retryTimer;
    private int _retryCount;

    public BirthdayTrayService(BirthdaySongPlayer songPlayer, ILogger<BirthdayTrayService> logger)
    {
        _songPlayer = songPlayer;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _songPlayer.PlaybackStateChanged += OnPlaybackStateChanged;
        EnsureTrayItem();
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

    private void EnsureTrayItem()
    {
        var service = IAppHost.Host?.Services.GetService<ITaskBarIconService>();
        if (service == null)
            return;

        _item = new NativeMenuItem();
        _item.Click += (_, _) => _songPlayer.Toggle();
        RefreshHeader();

        if (TryAdd(service))
            return;

        _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _retryTimer.Tick += (_, _) =>
        {
            if (TryAdd(service) || ++_retryCount >= 30)
            {
                _retryTimer?.Stop();
                _retryTimer = null;
            }
        };
        _retryTimer.Start();
    }

    private bool TryAdd(ITaskBarIconService service)
    {
        if (_item == null)
            return false;

        try
        {
            service.MoreOptionsMenuItems.Add(_item);
            RefreshHeader();
            return true;
        }
        catch (Exception)
        {
            // 宿主的「更多选项」菜单还没建好，过一会儿再试。
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
