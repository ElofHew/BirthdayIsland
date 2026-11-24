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
    /// <summary>随插件一起打包的生日歌文件名。放在 .cipx 根目录，和插件 dll 同级。</summary>
    public const string BundledSongFileName = "BirthdaySong.mp3";

    private readonly IAudioService _audioService;
    private readonly ILogger<BirthdaySongPlayer> _logger;

    private CancellationTokenSource? _current;

    public BirthdaySongPlayer(IAudioService audioService, ILogger<BirthdaySongPlayer> logger)
    {
        _audioService = audioService;
        _logger = logger;
    }

    /// <summary>
    /// 插件自带的生日歌的完整路径。
    /// </summary>
    /// <remarks>
    /// 用程序集所在目录而不是 <c>AppContext.BaseDirectory</c>：后者是宿主 ClassIsland 的目录，
    /// 插件实际被解压到 <c>Plugins/&lt;id&gt;/</c> 下，两者并不相同。
    /// </remarks>
    public static string BundledSongPath
    {
        get
        {
            string directory = Path.GetDirectoryName(typeof(BirthdaySongPlayer).Assembly.Location)
                               ?? AppContext.BaseDirectory;
            return Path.Combine(directory, BundledSongFileName);
        }
    }

    /// <summary>
    /// 解析实际要播放的文件：配置里填了能用的路径就用它，否则回退到插件自带的那首。
    /// </summary>
    /// <remarks>
    /// 回退而不是报错，是为了兼容老配置 —— 之前版本把某个绝对路径写进了默认值，
    /// 换台机器那个路径不存在，用户不应该为此看到「播不出声」。
    /// </remarks>
    public static string ResolveSongPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        return BundledSongPath;
    }

    /// <summary>
    /// 播放生日歌。不阻塞调用方，出错只记日志（音频设备缺失不该让提醒本身失败）。
    /// </summary>
    /// <param name="path">音频文件路径；空则使用插件自带的生日歌。</param>
    /// <param name="seconds">播放秒数；0 或负数表示完整播放。</param>
    public void Play(string path, int seconds)
    {
        Stop();

        string resolved = ResolveSongPath(path);
        if (!File.Exists(resolved))
        {
            _logger.LogWarning("生日歌文件不存在，跳过播放：{Path}", resolved);
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
                await _audioService.PlayAudioAsync(resolved, 1.0f, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 正常掐停
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "播放生日歌失败：{Path}", resolved);
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
