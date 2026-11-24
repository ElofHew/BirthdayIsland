# 生日显示插件

这是一个 ClassIsland 插件，用于从 Excel / CSV 表格导入姓名与生日，并在主界面显示今日生日人数。

## Github项目主页

https://github.com/PikaNova/BirthdayIsland
帮忙点个Star呗

## 功能预览

![功能预览](https://raw.githubusercontent.com/PikaNova/BirthdayIsland/github/Pictures/1.png)
![组件设置截图](https://raw.githubusercontent.com/PikaNova/BirthdayIsland/github/Pictures/2.png)
![导入内容截图](https://raw.githubusercontent.com/PikaNova/BirthdayIsland/github/Pictures/3.png)

## 功能

- 从 .xlsx / .csv 表格导入姓名和生日
- 自动保存生日数据到插件配置目录
- 在 ClassIsland 主界面显示今日生日人数（可选显示姓名）
- **定时提醒**：每天在指定时间通过 ClassIsland 提醒发出生日祝福，正文与遮罩时长可分别设置
- **生日歌**：提醒时可自动播放生日歌，可设定播放时长或完整播放
- **名单管理**：搜索、逐条编辑、删除、一键导出 CSV（不用为了改一个人重新导入表格）
- 处理闰年 2 月 29 日生日（可配置非闰年显示规则）

## 定时提醒

在设置页「定时提醒」一栏：

| 项目 | 说明 |
|------|------|
| 提醒时间 | 每天触发提醒的时刻（时 / 分） |
| 正文时长 | 浮在桌面上的那条祝福停留 3 / 5 / 10 秒 |
| 遮罩时长 | 全屏遮罩停留 1 / 2 / 3 秒 |
| 播放生日歌 | 提醒时同时播放；默认播放插件自带的 `BirthdaySong.mp3`，也可以另选本地音频文件 |
| 播放时长 | 30 / 45 / 60 / 75 秒，或完整播放 |

提醒文案：单人「张三，祝你生日快乐！」；多人「李四、王五，祝你们生日快乐！」

说明：

- 只在设定时刻触发一次，当天已经提醒过就不会再响（重启应用也不会重复）。
- 应用在提醒时刻之后 5 分钟内启动会补发一次；超过 5 分钟就跳过，避免晚上开电脑突然弹早上的提醒。
- 提醒是通过插件注册的「生日提醒」提醒提供方发出的，可以在 ClassIsland 的【设置 → 提醒】里单独给它调音效和特效。
- 生日歌走宿主自带的音频服务播放，运行环境里不需要额外的播放器。
- 生日歌文件随插件一起打包（`.cipx` 根目录的 `BirthdaySong.mp3`），装好就能用；换成自己的音乐点「选择…」，想换回自带的点「恢复默认」。

## 名单管理

设置页底部的「生日名单」支持：

- 按姓名 / 班级 / 备注搜索
- 每行「编辑」「删除」：编辑只改这一条，不用重新导入整张表
- 「导出 CSV」：把当前全部数据导出为 UTF-8（带 BOM）的 CSV，Excel 直接打开不乱码；导出的生日沿用导入时的原始写法（例如 `3月27日`），再导入不会凭空多出一个年份


## 表格格式

第一行必须包含表头：

| 姓名 | 生日 |
|------|------|
| 张三 | 2008-03-27 |
| 李四 | 3月27日 |

支持 `.xlsx` 与 `.csv`。旧版二进制 `.xls` 不支持——ClosedXML 只读 OpenXML 格式，请先在 Excel 里「另存为」`.xlsx` 或 `.csv`。

CSV 请用逗号分隔（Excel 默认导出的就是逗号）。编码支持带 BOM 的 UTF-8 以及中文 Windows 上 Excel 默认导出的 GBK 系编码，不需要手动转码。

可自动识别的列名：

- 姓名列：姓名 / 名字 / Name
- 生日列：生日 / 出生日期 / Birthday / BirthDate
- （可选）班级列：班级 / Class
- （可选）备注列：备注 / Remark

支持的生日格式：

- 2008-03-27
- 2008/03/27
- 03-27
- 03/27
- 3月27日
- 2008年3月27日

## 使用方法

1. 安装插件
2. 打开 ClassIsland 设置
3. 进入“生日显示插件”
4. 点击“导入表格”，选择 .xlsx 或 .csv 文件
5. 在【应用设置】->【组件】中，将“今日生日”组件添加到主界面组件栏
6. （可选）在组件设置中开启/关闭姓名显示，设置最多显示姓名数
7. （可选）在设置页开启定时提醒，设定时间与生日歌
8. （可选）在设置页选择闰日（2 月 29 日）在非闰年的显示方式

## 数据存储

生日数据保存在插件配置目录下的 `birthdays.json`，启动时自动读取，导入/删除/清空后自动保存。

## 开发说明

```bash
# 只编译
dotnet build BirthdayIsland.csproj -c Release

# 编译 + 打包成 .cipx（需要 PowerShell 5.1+）
pwsh ./build.ps1
```

> 工程用 `ProjectReference` 引用 `../../ClassIsland.Core`，所以本仓库必须放在 ClassIsland 源码树的 `plugins/BirthdayIsland` 下。
> 打包清单在 `pack-include.txt`（ClosedXML 那一串第三方 dll + 生日歌），产物输出到 `cipx/`。
> 生日歌源码放在 `Assets/BirthdaySong.mp3`，构建时按 Link 平铺到输出目录，运行时按程序集目录查找。

使用Opus 4.8辅助开发