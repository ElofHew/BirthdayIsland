using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using BirthdayIsland.Services;

namespace BirthdayIsland.Automation;

/// <summary>
/// 自动化行动：立即发出一次生日提醒。
/// </summary>
/// <remarks>
/// 和插件自带的定时提醒共用同一条发送路径，所以：
/// 发完会记「今天已提醒」，同一天插件自带的定时提醒就不会再发；
/// 反过来，定时提醒已经发过的日子，这个行动仍然会执行（用户显式要求的）。
/// </remarks>
[ActionInfo("birthdayisland.reminder.announce", "发送生日提醒", "\uE7BA", defaultGroupToMenu: "生日")]
public class BirthdayAnnounceAction : ActionBase
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();

        // 提供方实例只以 IHostedService 的形式存在，按类型捞（见 BirthdayNotificationProvider.TryGetCurrent）。
        BirthdayNotificationProvider.TryGetCurrent()?.AnnounceNow();
    }
}
