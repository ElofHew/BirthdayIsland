using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BirthdayIsland.Services;
using BirthdayIsland.ViewModels;
using BirthdayIsland.Views.Components;
using BirthdayIsland.Views.SettingsPages;

namespace BirthdayIsland;

[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // 数据与导入服务（单例，全局共享）
        services.AddSingleton<BirthdayDataService>();
        services.AddSingleton<ExcelImportService>();

        // 生日歌播放（复用宿主的 IAudioService）与定时提醒的提醒提供方。
        // 提醒提供方注册进来后会出现在「设置 → 提醒」里，可以单独调音效和特效。
        services.AddSingleton<BirthdaySongPlayer>();
        services.AddNotificationProvider<BirthdayNotificationProvider>();

        // 设置页 ViewModel
        services.AddTransient<BirthdaySettingsPageViewModel>();

        // 注册设置页
        services.AddSettingsPage<BirthdaySettingsPage>();

        // 注册主界面组件（带组件级设置控件）
        // 官方 API：AddComponent<TComponent, TSettingsControl>()
        services.AddComponent<BirthdayTodayComponent, BirthdayTodayComponentSettingsControl>();
    }

    // 注：本机 SDK（ClassIslandPluginSdkVersion = 1.7.106.2-dev-v2）的 PluginBase
    // 没有可重写的 OnShutdown() 方法，故不再重写（数据在每次变更时已即时落盘，无需退出处理）。
    // 若你升级到支持 OnShutdown 的新版 SDK，可再加回 override。
}