using System;
using BirthdayIsland.Services;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;

namespace BirthdayIsland.Automation;

/// <summary>
/// 自动化触发器：生日提醒发出时。
/// </summary>
/// <remarks>
/// 定时提醒和「发送生日提醒」行动都会走到提醒提供方的 <c>Announce</c>，
/// 那里统一抛一次事件，所以两种来源都能触发。
/// <para>
/// 提供方实例在 <see cref="Loaded"/> 里取，不在构造函数里取：
/// 工作流可能在宿主还没把托管服务全部起来的时候就被加载，
/// 那时候按类型还捞不到提醒提供方。
/// </para>
/// </remarks>
[TriggerInfo("birthdayisland.reminder.announced", "生日提醒触发时", "\uE7BA")]
public class BirthdayReminderTrigger : TriggerBase
{
    private BirthdayNotificationProvider? _provider;

    public override void Loaded()
    {
        _provider = BirthdayNotificationProvider.TryGetCurrent();
        if (_provider != null)
            _provider.Announced += OnAnnounced;
    }

    public override void UnLoaded()
    {
        if (_provider != null)
            _provider.Announced -= OnAnnounced;

        _provider = null;
    }

    private void OnAnnounced(object? sender, BirthdayAnnouncementEventArgs e) => Trigger();
}
