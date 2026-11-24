using System.Threading.Tasks;
using BirthdayIsland.Services;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;

namespace BirthdayIsland.Automation;

/// <summary>
/// 自动化行动：播放生日歌。
/// </summary>
/// <remarks>
/// 播的是插件设置里那一套（路径 + 时长），和定时提醒、托盘菜单共用同一个播放器实例，
/// 所以在任何一处点「停止」都能停掉这里发起的播放。
/// 重写了 OnRevert，宿主会自动把这个行动标成「支持恢复」：工作流恢复时把歌停掉。
/// </remarks>
[ActionInfo("birthdayisland.song.play", "播放生日歌", "\uE8D6", defaultGroupToMenu: "生日")]
public class BirthdaySongPlayAction : ActionBase
{
    private readonly BirthdaySongPlayer _songPlayer;

    public BirthdaySongPlayAction(BirthdaySongPlayer songPlayer)
    {
        _songPlayer = songPlayer;
    }

    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        _songPlayer.PlayFromConfig();
    }

    protected override async Task OnRevert()
    {
        await base.OnRevert();
        _songPlayer.Stop();
    }
}
