using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace BirthdayIsland.Services;

/// <summary>
/// 生日歌播放器。复用宿主的 <see cref="IAudioService"/>（MiniAudio 后端，mp3 原生支持），
/// 不额外绑定播放库。
/// </summary>
/// <remarks>
/// 宿主只提供「播放到结束或取消为止」这一个能力，没有按秒停止的接口，
/// 所以定时停止靠 <see cref="CancellationTokenSource.CancelAfter"/> 实现。
/// </remarks>
public class BirthdaySongPlayer
{
    private readonly IAudioService _audioService;
    private readonly ILogger<BirthdaySongPlayer> _logger;

    private CancellationTokenSource? _current;

    public BirthdaySongPlayer(IAudioService audioService, ILogger<BirthdaySongPlayer> logger)
    {
        _audioService = audioService;
        _logger = logger;
    }

    /// <summary>
    /// 播放生日歌。不阻塞调用方，出错只记日志（音频设备缺失不该让提醒本身失败）。
    /// </summary>
    /// <param name="path">音频文件路径。</param>
    /// <param name="seconds">播放秒数；0 或负数表示完整播放。</param>
    public void Play(string path, int seconds)
    {
        Stop();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _logger.LogWarning("生日歌文件不存在，跳过播放：{Path}", path);
            return;
        }

        var cts = new CancellationTokenSource();
        _current = cts;

        // CancelAfter 到点后，宿主那边的 PlayAudioAsync 会从 Wait 里退出并释放播放器，
        // 音频随之停止 —— 这是唯一能「播一半掐掉」的办法。
        if (seconds > 0)
            cts.CancelAfter(TimeSpan.FromSeconds(seconds));

        _ = Task.Run(async () =>
        {
            try
            {
                await _audioService.PlayAudioAsync(path, 1.0f, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 正常掐停
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "播放生日歌失败：{Path}", path);
            }
            finally
            {
                if (ReferenceEquals(_current, cts))
                    _current = null;
                cts.Dispose();
            }
        });
    }

    /// <summary>停止当前播放（如果有）。</summary>
    public void Stop()
    {
        var current = _current;
        if (current == null)
            return;

        try
        {
            current.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经播完并释放了
        }
    }
}
