using System;
using System.Collections.Generic;

namespace BirthdayIsland.Services;

/// <summary>
/// 一次生日提醒的内容。
/// </summary>
/// <remarks>
/// 目前只带今天过生日的人名。自动化触发器没法把数据传给行动项，
/// 所以这个参数主要是给日志和将来的扩展留个位置。
/// </remarks>
public class BirthdayAnnouncementEventArgs : EventArgs
{
    public BirthdayAnnouncementEventArgs(IReadOnlyList<string> names)
    {
        Names = names;
    }

    /// <summary>本次提醒里过生日的人。</summary>
    public IReadOnlyList<string> Names { get; }
}
