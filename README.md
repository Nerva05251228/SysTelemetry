# SysTelemetry (`SYS.TELEMETRY`)

轻量级 Windows 11 三层系统遥测与任务栏硬件监控工具（WPF + Native Win32 + LibreHardwareMonitorLib），支持零配置外置编译（直接使用系统内置 `.NET Framework 4.x csc.exe`）、固定列宽防抖动排版、桌面单行透明鼠标穿透角标与任务栏双侧停靠。

---

## 核心特性

### 1. 三层分离交互架构

1. **任务栏监控条 (`TaskbarBarWindow`)**
   - 始终可点击交互（不开启鼠标穿透），右键打开快捷控制菜单，左键展开或收起详情面板。
   - **防遮挡方案**：紧凑固定列宽（`218px - 245px`）+ 默认**任务栏左侧空旷区停靠**（自动读取 `TaskbarDa` 注册表键值避让 Win11 小组件），支持一键切换 `左侧 / 右侧` 停靠与 `-200px ~ +200px` 横向像素微调。

2. **点击扩展遥测面板 (`TelemetryPopoverWindow`)**
   - **60S TRACE**：`CPU`、`GPU`、`TEMP` 三通道 60 秒实时趋势曲线。
   - **传感器卡片**：`CPU`（负载 / 封装温度 / 功耗 / 主频）、`GPU`（负载 / 核心温度 / 功耗 / 显存）、`MEM`（已用占比 / 可用容量）、`DISK`（NVMe 温度 / 读写速率）。
   - **今日流量摘要与独立统计页 (`SYS.TRAFFIC`)**：主面板透出实时网速与今日全局/代理汇总，点击或通过顶栏图标切换至 `SYS.TRAFFIC`，查看按天 / 按月（支持自定义每月重置日）的全局与代理总流量、下载/上行拆分、直连占比及 14 天 / 12 周期堆叠柱状图与明细列表。
   - **高占用进程 (`PROCESSES`)**：按 CPU / 内存 / 磁盘 IO 实时采样展示前 5 个高资源占用进程。
   - **头部快捷工具栏**：桌面单行悬浮角标开关、一键内存工作集清理 `TRIM`（含按钮弹性缩放旋转及 `MEM` 卡片翠绿脉冲反馈动画）、`SYS.TRAFFIC` 流量统计页切换、内嵌 `SYS.CONFIG` 设置页切换、关闭按钮。

3. **桌面单行透明角标 (`DesktopOverlayWindow`)**
   - 纯透明无边框单行基础读数，无波形图占用视野，常驻桌面角位（默认右上角）。
   - **拖拽与鼠标穿透**：在设置或菜单中取消固定位置后可自由拖拽摆放；固定位置后自动施加 `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` 实现全鼠标穿透，不影响底层窗口点击。

### 2. 四套固定列宽防抖皮肤

所有任务栏条与桌面角标数值列均采用 `Tabular` 等宽数字对齐与固定列宽封装，网速（`0K` -> `99.9M`）及百分比切换时横宽零抖动：

| 皮肤 (`SkinStyle`) | 风格说明 | 任务栏宽度 |
| :--- | :--- | :---: |
| **黑白 (`Hud`)** | 无边框极客黑白灰 HUD + 彩色 CPU 实时小波形 | `243 px` |
| **Fluent** | Win11 卡片边框 + 状态点指示 | `237 px` |
| **色条 (`Lite`)** | 带微型负载进度条的紧凑卡片 | `245 px` |
| **极简 (`Minimal`)** | 高密度透明排版 | `218 px` |

### 3. 系统集成与体验细节

- **全屏自动隐藏**：前台存在全屏游戏或全屏视频窗口时自动隐藏任务栏条与桌面角标。
- **Win11 深浅色自动适配**：监听 `UserPreferenceChanged` 并自动跟随系统任务栏深浅主题（亦可在设置中强制深色或浅色）。
- **静默高权限开机自启**：以管理员身份运行并开启自启时，自动写入 Windows 任务计划程序 `SysTelemetry_AutoStart`（`/RL HIGHEST`），开机登录静默启动且免 UAC 弹窗；普通权限下自动回退到当前用户注册表 `Run` 项。
- **单实例保护**：通过命名互斥锁与唤醒事件防止重复启动，再次运行可唤醒切换详情面板。
- **全局热键**：
  - `Ctrl + Alt + M`：展开 / 收起详情面板
  - `Ctrl + Alt + L`：固定 / 解锁桌面角标位置（切换鼠标穿透）

---

## 快速使用

### 方式一：下载预编译版本

从 [Releases](https://github.com/Nerva05251228/SysTelemetry/releases) 下载压缩包并解压到任意目录（例如 `D:\Software\SysTelemetry\`），保证目录下包含以下文件后直接运行 `SysTelemetry.exe`：

- `SysTelemetry.exe`
- `LibreHardwareMonitorLib.dll`
- `HidSharp.dll`

> 提示：部分主板 CPU 封装温度/功耗传感器及 NVMe 硬盘温度读取需要管理员权限。若发现温度显示为空，可右键 `SysTelemetry.exe` 以管理员身份运行；开启「开机静默自启」后将自动以高权限静默拉起。

### 方式二：源码零依赖编译

无需安装 Visual Studio 或 .NET SDK，仅需 Windows 10 / 11 (x64) 自带的 `.NET Framework 4.x` 即可在数秒内完成编译：

```powershell
git clone https://github.com/Nerva05251228/SysTelemetry.git
cd SysTelemetry
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

编译产物会输出至 `dist\` 目录（包含 `SysTelemetry.exe` 及运行所需的两个依赖库）。

---

## 配置文件

用户配置保存于：

```text
%APPDATA%\SysTelemetry\config.json
```

主要配置字段说明：

| 字段 | 默认值 | 说明 |
| :--- | :--- | :--- |
| `Skin` | `Hud` (`0`) | 界面皮肤（`0=Hud`、`1=Fluent`、`2=Lite`、`3=Minimal`） |
| `Theme` | `Auto` (`0`) | 主题模式（`0=Auto`、`1=Dark`、`2=Light`） |
| `TaskbarDock` | `Left` (`0`) | 任务栏停靠位置（`0=Left`、`1=Right`） |
| `TaskbarOffsetX` | `0` | 任务栏横向偏移微调（`-200 ~ +200 px`） |
| `DesktopOverlayEnabled` | `true` | 是否显示桌面单行透明角标 |
| `DesktopLocked` | `true` | 是否固定桌面角标位置并启用鼠标穿透 |
| `DesktopOpacity` | `0.92` | 桌面角标字体与前景透明度（`0.35 ~ 1.00`） |
| `FullscreenAutoHide` | `true` | 全屏应用时是否自动隐藏浮窗 |
| `PollIntervalMs` | `1000` | 采样周期（`500` / `1000` / `2000` ms） |
| `TempWarnThreshold` | `80` | 高温预警阈值（`75` / `80` / `85` °C） |
| `TrafficResetDay` | `1` | 每月流量统计周期重置日（`1 ~ 31`，小月自动按月底对齐） |
| `ProxyProcessNames` | `"sing-box,xray,mihomo,v2ray"` | 计入代理流量的进程名列表（逗号分隔，通过内核 ETW 排除回环统计） |

每日流量历史记录持久化保存于 `%APPDATA%\SysTelemetry\traffic.json`（保留近 400 天，变更重置日后自动即时重算当期及历史月度周期）。

---

## 项目结构

```text
SysTelemetry/
├── App.cs                        # 应用生命周期、单实例互斥、托盘菜单与热键调度
├── app.manifest                  # Per-Monitor DPI v2 与 UAC Manifest 配置
├── build.ps1                     # 基于系统 csc.exe 的零外部工具链构建脚本
├── Interop/
│   └── NativeMethods.cs          # Win32 P/Invoke（窗口穿透、置顶、全屏检测、内存清理）
├── Models/
│   ├── AppConfig.cs              # JSON 配置序列化、校验与持久化
│   ├── TelemetrySnapshot.cs      # 遥测快照与进程指标模型
│   └── TrafficStats.cs           # 每日/周期流量统计模型与持久化结构
├── Services/
│   ├── AutoStartService.cs       # 任务计划程序 / 注册表开机自启管理
│   ├── TelemetryService.cs       # LibreHardwareMonitor + 性能计数器硬件采样引擎
│   ├── ThemeHelper.cs            # 深浅主题调色板、状态着色与固定宽度格式化
│   └── TrafficMonitorService.cs  # 物理网卡全局统计 + Windows 内核 ETW 代理流量统计引擎
├── UI/
│   ├── DesktopOverlayWindow.cs   # 桌面单行透明悬浮角标窗口
│   ├── IconFactory.cs            # 矢量 SVG 图标与动态托盘图标生成器
│   ├── TaskbarBarWindow.cs       # 任务栏监控条定位、避让与交互窗口
│   ├── TelemetryPopoverWindow.cs # 详情弹窗、SYS.CONFIG 设置页与 TRIM 动效
│   └── WidgetVisualBuilder.cs    # 四套皮肤与单行悬浮视觉树构建器
└── lib/
    ├── HidSharp.dll
    └── LibreHardwareMonitorLib.dll
```

---

## 开源协议

- 本项目主体代码采用 [MIT License](LICENSE) 开源。
- 第三方硬件监控依赖库（`LibreHardwareMonitorLib` / `HidSharp`）遵循各自原始开源协议，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
