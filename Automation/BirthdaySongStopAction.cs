using System.Threading.Tasks;
using BirthdayIsland.Services;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;

namespace BirthdayIsland.Automation;

/// <summary>
/// 自动化行动：停止播放生日歌。
/// </summary>
/// <remarks>
/// 「播放生日歌」行动本身支持恢复（工作流恢复时会停），这条是给不能靠恢复收尾的场景用的，
/// 比如「A 触发器开播、B 触发器停播」这种成对的编排。
/// 没在播时执行它什么也不做，不会报错。
/// </remarks>
[ActionInfo("birthdayisland.song.stop", "停止播放生日歌", "\uE71A", defaultGroupToMenu: "生日")]
public class BirthdaySongStopAction : ActionBase
{
    private readonly BirthdaySongPlayer _songPlayer;

    public BirthdaySongStopAction(BirthdaySongPlayer songPlayer)
    {
        _songPlayer = songPlayer;
    }

    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        _songPlayer.Stop();
    }
}
