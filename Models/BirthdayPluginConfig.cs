using System;
using System.Collections.Generic;

namespace BirthdayIsland.Models;

public class BirthdayPluginConfig
{
    public List<BirthdayPerson> People { get; set; } = new();

    public DateTime? LastImportTime { get; set; }

    public string? LastImportFileName { get; set; }

    public LeapDayDisplayMode LeapDayDisplayMode { get; set; } = LeapDayDisplayMode.OnlyOnFeb29;

    /// <summary>定时提醒设置。旧版本配置文件里没有这一段，反序列化时会走属性默认值。</summary>
    public BirthdayReminderConfig Reminder { get; set; } = new();
}

public enum LeapDayDisplayMode
{
    /// <summary>仅在闰年的 2 月 29 日显示。</summary>
    OnlyOnFeb29,

    /// <summary>非闰年时在 2 月 28 日显示。</summary>
    ShowOnFeb28WhenNonLeapYear,

    /// <summary>非闰年时在 3 月 1 日显示。</summary>
    ShowOnMar1WhenNonLeapYear
}