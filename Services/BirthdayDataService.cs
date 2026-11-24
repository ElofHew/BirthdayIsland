using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using BirthdayIsland.Models;
using ClassIsland.Shared.Helpers;

namespace BirthdayIsland.Services;

public class BirthdayDataService
{
    private readonly string _configPath;

    /// <summary>数据变化（导入 / 删除 / 清空 / 设置变更）后触发，供组件与设置页刷新。</summary>
    public event EventHandler? DataChanged;

    public ObservableCollection<BirthdayPerson> People { get; } = new();

    public BirthdayPluginConfig Config { get; private set; } = new();

    public DateTime? LastImportTime => Config.LastImportTime;

    public string? LastImportFileName => Config.LastImportFileName;

    // 入口类 Plugin 会被注册进 IoC，可直接注入以获取插件配置目录。
    public BirthdayDataService(Plugin plugin)
    {
        _configPath = Path.Combine(plugin.PluginConfigFolder, "birthdays.json");
        Load();
    }

    public IReadOnlyList<BirthdayPerson> GetTodayBirthdays(DateTime? date = null)
    {
        var today = (date ?? DateTime.Now).Date;
        return People.Where(p => IsBirthdayToday(p.Birthday, today)).ToList();
    }

    public int GetTodayBirthdayCount(DateTime? date = null) => GetTodayBirthdays(date).Count;

    /// <summary>覆盖导入：清空现有数据并写入新数据。</summary>
    public void AddOrReplacePeople(IEnumerable<BirthdayPerson> people, string? fileName = null)
    {
        People.Clear();
        foreach (var person in people)
            People.Add(person);

        Config.LastImportTime = DateTime.Now;
        Config.LastImportFileName = fileName;
        Save();
    }

    public void RemovePerson(BirthdayPerson person)
    {
        if (People.Remove(person))
            Save();
    }

    /// <summary>
    /// 用编辑后的对象替换列表中原来那条。
    /// </summary>
    /// <remarks>
    /// <see cref="BirthdayPerson"/> 是不带变更通知的普通类，就地改属性界面不会更新，
    /// 所以走 ObservableCollection 的「替换」语义 —— 顺便保证列表顺序不变。
    /// </remarks>
    public void ReplacePerson(BirthdayPerson original, BirthdayPerson updated)
    {
        int index = People.IndexOf(original);
        if (index < 0)
            return;

        People[index] = updated;
        Save();
    }

    /// <summary>
    /// 记录今天已经发过提醒。存盘是为了应用在同一天重启后不再重复提醒。
    /// </summary>
    public void MarkReminderFired(DateTime date)
    {
        Config.Reminder.LastFiredDate = date.Date;
        Save();
    }

    public void Clear()
    {
        People.Clear();
        // 「上次导入」是跟着这批数据走的，数据清空了却还留着文件名，
        // 设置页就会显示「总人数 0 / 上次导入 xxx.xlsx」，看着像没清干净。
        Config.LastImportTime = null;
        Config.LastImportFileName = null;
        Save();
    }

    public void SetLeapDayDisplayMode(LeapDayDisplayMode mode)
    {
        Config.LeapDayDisplayMode = mode;
        Save();
    }

    public void Load()
    {
        try
        {
            // 文件不存在时 ConfigureFileHelper 会返回默认实例（自动创建空配置）。
            Config = ConfigureFileHelper.LoadConfig<BirthdayPluginConfig>(_configPath) ?? new BirthdayPluginConfig();
        }
        catch (Exception)
        {
            // JSON 损坏：回退到空配置，避免插件崩溃。
            Config = new BirthdayPluginConfig();
        }

        People.Clear();
        foreach (var person in Config.People)
            People.Add(person);

        // 旧版本配置文件里没有 Reminder 这一段；如果哪天手改 json 写成 "Reminder": null，
        // 这里兜一下，否则设置页和提醒服务都会空引用。
        Config.Reminder ??= new BirthdayReminderConfig();

        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        Config.People = People.ToList();
        try
        {
            ConfigureFileHelper.SaveConfig(_configPath, Config);
        }
        catch (Exception)
        {
            // 落盘失败（权限 / 占用）时不抛出，仅保留内存数据。
        }

        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsBirthdayToday(DateTime birthday, DateTime today)
    {
        // 闰日特殊处理
        if (birthday.Month == 2 && birthday.Day == 29)
        {
            if (DateTime.IsLeapYear(today.Year))
                return today.Month == 2 && today.Day == 29;

            return Config.LeapDayDisplayMode switch
            {
                LeapDayDisplayMode.ShowOnFeb28WhenNonLeapYear => today.Month == 2 && today.Day == 28,
                LeapDayDisplayMode.ShowOnMar1WhenNonLeapYear => today.Month == 3 && today.Day == 1,
                _ => false // OnlyOnFeb29
            };
        }

        // 普通情况：只比较月、日
        return birthday.Month == today.Month && birthday.Day == today.Day;
    }
}