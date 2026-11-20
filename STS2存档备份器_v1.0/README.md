# STS2 存档备份器（旧版整包格式）

给《杀戮尖塔 2》（Slay the Spire 2，Steam AppID 2868840）用的本地存档备份工具。

它按小黑盒**旧版**的方式工作：**一个档案打成一个完整 zip**，文件名保持
`profile3--<毫秒时间戳>----.zip` 的格式。不会像小黑盒 v2 那样卡在 500 个文件，
所以 `history` 里的逐局记录能完整备份。

![界面](docs/screenshot.png)

## 快速开始

1. 把压缩包解压到一个固定文件夹（不要直接在压缩包里运行），双击 `STS2存档备份器.exe`（免安装；需要 .NET Framework 4.8，Windows 10/11 自带）。
2. 程序会自动找 `C:\steam\userdata\<账号>\2868840\remote`；找不到就点“浏览...”手动选。
3. 输出目录默认是 `文档\STS2存档备份\<账号>\2868840`，可以点“更改...”换位置。
4. 点 **立即备份**，得到类似 `profile3--1794849376265----.zip` 的文件。
5. 建议勾选 **游戏退出后自动备份**，之后正常玩就行。

> 首次运行如果 Windows SmartScreen 提示“未知发布者”，点“更多信息 → 仍要运行”。
> 程序没有签名，但源码就在 `src\Sts2SaveBackup.cs`，可以自行编译核对。

## 功能

- **当前档案 / 全部档案**：默认只备份 `profile.save` 指向的当前档案；勾选后 profile1/2/3 各打一个包。
- **完整目录**：`saves\`、`history\*.run`、`mods\`、`replays\latest.mcr` 全部进包。
- **包含 profile.save**：包根目录额外写入一个指向该档案的 `profile.save`，还原到新机器也能直接进对档案；同时带上 `settings.save.backup`。
- **内容没变就跳过**：按“路径 + 大小 + 修改时间”算指纹，和最近一份备份相同就不重复打包。
- **保留份数**：每个档案只保留最近 N 份（默认 30，0 = 不限）。只删本工具生成、带 `.meta.txt` 的备份，**不会误删小黑盒旧包**。
- **自动备份**：
  - 每隔 N 分钟；
  - 检测到 `SlayTheSpire2.exe` 退出后补一次；
  - 只在游戏未运行时执行，避免打包到写了一半的存档。
- **备份列表**：同时列出本工具备份和小黑盒旧版整包 zip；双击一行即可还原。
- **安全还原**：还原前把将被覆盖的现有文件复制到 `输出目录\_restore_backup_<时间>\`；旧包没有 `profile.save` 时会询问是否切换当前档案。
- **命令行模式**：`sts2-backup-cli.exe`，方便做计划任务或脚本。

## 备份包结构

```
profile3--1794849376265----.zip
├─ profile3/
│  ├─ saves/progress.save
│  ├─ saves/progress.save.backup
│  ├─ saves/prefs.save
│  ├─ saves/history/*.run          （544 局，全部在内）
│  ├─ saves/mods/GlutonsAscensions/ascension_progress.save
│  └─ replays/latest.mcr
├─ profile.save                    （可选，指向 profile3）
└─ settings.save.backup            （可选）
```

和旧版小黑盒包的布局一致（`profile3/...` 开头），直接解压到 `remote` 根目录即可；
本工具只是额外放了 `profile.save` 和 `settings.save.backup`，方便独立还原。

## 和这次小黑盒 v2 的对比

| | 小黑盒 v2 | 本工具 |
| --- | --- | --- |
| 打包方式 | 每个文件一个 zip + manifest | 每个档案一个完整 zip |
| profile3 历史 | 492 局（被 500 上限截断） | **544 局全量** |
| 文件数上限 | 500 | 无 |
| 回放 | 不含 | 含 `replays/latest.mcr` |
| 其他档案位 | 不含 | 可勾选“全部档案位” |
| 自动备份 | 由小黑盒触发 | 定时 / 游戏退出，本地可控 |
| 还原粒度 | 可按文件 | 整包还原（还原前自动留旧文件） |
| 兼容旧包 | — | 可直接还原小黑盒旧版 zip |

## 还原步骤

1. 退出游戏。
2. Steam → 库 → 右键游戏 → 属性 → 通用 → 暂时关闭“Steam 云”（或整个 Steam 离线）。
3. 打开本工具，在列表里选一份备份，点 **还原所选**。
4. 如果是小黑盒旧包（没有 `profile.save`），按提示决定是否切换当前档案。
5. 启动游戏确认；没问题后再开 Steam 云，若询问冲突选**本地文件**。

## 自动备份说明

- 程序需要保持运行（可以最小化）。
- 每 10 秒检查一次游戏进程；检测到退出后自动备份。
- 定时备份到点时如果游戏还在运行，会跳过本次并写日志，等下次。
- 手动备份时如果游戏在运行，会弹窗警告，仍可选择继续。

## 命令行

```powershell
# 查看帮助
.\sts2-backup-cli.exe --help

# 备份当前档案（默认输出到 文档\STS2存档备份\...）
.\sts2-backup-cli.exe --source "C:\steam\userdata\1021812450\2868840\remote" --output "D:\我的备份"

# 备份全部档案，每个档案保留 20 份
.\sts2-backup-cli.exe --source "...\remote" --output "D:\我的备份" --all --keep 20

# 列出备份
.\sts2-backup-cli.exe --output "D:\我的备份" --list

# 还原指定备份，并切换当前档案
.\sts2-backup-cli.exe --source "...\remote" --output "D:\我的备份" --restore "D:\我的备份\profile3--1794849376265----.zip" --switch
```

常用参数：`--no-root`（不写 profile.save/settings）、`--no-skip`（内容没变也新建）、`--keep N`。

## 文件位置

- 设置与日志：`%APPDATA%\STS2SaveBackup\settings.ini`、`backup.log`
  - 如果 `%APPDATA%` 不可写，会自动改用 exe 旁边的 `data\`（便携模式）。
- 默认备份目录：`文档\STS2存档备份\<Steam账号>\2868840`。
- 每个 zip 旁边有一个 `.meta.txt`，记录档案、文件数、原始大小、指纹和来源路径；删除备份时可一起删。

## 从源码编译

需要 Windows 自带的 .NET Framework 4.x（`csc.exe`）。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出：

- `dist\STS2存档备份器.exe`（GUI）
- `dist\sts2-backup-cli.exe`（命令行）

## 已验证

- 备份 profile3：552 个条目（550 个档案文件 + profile.save + settings.save.backup），47.2 MB → 3.1 MB；
- 552 个条目与实机存档逐文件 SHA-256 比对全部一致；
- 544 局历史全部进包，没有 500 上限；
- 还原到空目录：552 个文件哈希全部一致；
- 还原小黑盒旧版 `profile3--1794720774229----.zip`：540 个文件正常展开，可切换档案；
- 去重、保留份数、GUI 点击备份、列表显示均已实测。

## 注意

- 本工具不备份 `remote\modded\` 这个 Mod 平行目录，也不备份 `profile.save.backup`。需要的话可以再加。
- 备份文件不会自动上传云端，建议把输出目录放进 OneDrive/坚果云等同步盘。
- 不要在游戏运行时依赖自动备份；程序会跳过，但手动强制备份仍可能拿到不一致的快照。
