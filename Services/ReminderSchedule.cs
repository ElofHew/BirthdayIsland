using System;

namespace BirthdayIsland.Services;

/// <summary>
/// 定时提醒的触发判定。
/// </summary>
/// <remarks>
/// 抽成纯函数是为了能脱离 ClassIsland 环境单独验证时间窗口逻辑 —— 这类「到点触发一次」
/// 的代码最容易出的问题就是重启后重复触发、或者过了点还在补触发。
/// </remarks>
public static class ReminderSchedule
{
    /// <summary>
    /// 目标时刻之后还能补发多久。
    /// </summary>
    /// <remarks>
    /// 应用不一定在到点那一刻就开着：8:00 的提醒，8:03 才开电脑也应该补一条。
    /// 但窗口不能大 —— 晚上 9 点开电脑突然弹「早上好，张三生日快乐」比漏提醒更奇怪。
    /// 5 分钟足够覆盖「开机 → ClassIsland 起来 → 首次检查」这点延迟。
    /// </remarks>
    public static readonly TimeSpan CatchUpWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 判断此刻是否应该发出今天的提醒。
    /// </summary>
    /// <param name="now">当前本地时间。</param>
    /// <param name="hour">设定的时（0~23）。</param>
    /// <param name="minute">设定的分（0~59）。</param>
    /// <param name="lastFiredDate">上次发出提醒的日期，未发过为 null。</param>
    public static bool ShouldFire(DateTime now, int hour, int minute, DateTime? lastFiredDate)
    {
        if (hour is < 0 or > 23 || minute is < 0 or > 59)
            return false;

        // 今天已经发过（或者刚发完就重启了）
        if (lastFiredDate?.Date == now.Date)
            return false;

        var target = now.Date.AddHours(hour).AddMinutes(minute);
        if (now < target)
            return false;

        return now - target <= CatchUpWindow;
    }
}
