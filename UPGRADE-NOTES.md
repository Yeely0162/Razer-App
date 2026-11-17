# 改造说明 · Razer Chroma WLED Studio

原版：fu-raz / Razer-Chroma-WLED-Connect-App 0.0.2（WPF / .NET 5 / 2512 行 / 无分层）
改造后：WPF / .NET 8 / Core 类库 + 界面壳 / MVVM

原项目完整备份在 `G:\Razer-Wled\backup\Razer-Chroma-WLED-Connect-App-0.0.2-original`，
需要回滚时把备份目录覆盖回原位置即可。

## 1. 目录结构

```
Razer Chroma WLED Connect App.sln
RazerChromaWLEDConnect/              ← WPF 外壳（XAML + 平台服务）
  Theme/Theme.xaml                   配色、控件样式（唯一一份）
  Views/*.xaml                       五个页面
  Controls/LedStrip.cs               WriteableBitmap 灯带预览
  Services/                          雷蛇 SDK 桥、mDNS 发现、注册表、电源事件
  Lenovo/LenovoKeyboard.cs           HID 键盘（保留原功能）
RazerChromaWLEDConnect.Core/         ← 无 WPF 依赖，可单独编译与测试
  Core/  Rgb, ColorInterpolationEngine, LedMappingEngine, WledUdpProtocol,
         WledTransport, FrameSlot, ChromaInputManager, RenderPipeline,
         PerformanceMonitor, DeviceManager
  Base/RGBBase.cs                    设备基类（保留旧字段名，兼容 settings.xml）
  WLED/WLEDModule.cs, WLEDSegment.cs
  ViewModels/                        Main / Device / Channel / Segment / Performance / Settings
  AppSettings.cs                     settings.xml 模型
```

依赖方向只有一条：`RazerChromaWLEDConnect` → `RazerChromaWLEDConnect.Core`。
Core 里不出现任何 `System.Windows` 类型，所以协议和映射逻辑可以脱离界面测试。

## 2. 架构对照（任务书第六节）

| 任务书模块 | 实际位置 |
|---|---|
| ChromaInputProvider | `Services/ChromaBroadcastLink.cs`（实现 Core 的 `IChromaBroadcastLink`，接口统一，可换其他输入源） |
| ChromaInputManager | `Core/ChromaInputManager.cs` |
| ColorInterpolationEngine | `Core/ColorInterpolationEngine.cs` |
| LEDMappingEngine | `Core/LedMappingEngine.cs` |
| WLEDTransport | `Core/WledTransport.cs` + `Core/WledUdpProtocol.cs` |
| DeviceManager | `Core/DeviceManager.cs` |
| PerformanceMonitor | `Core/PerformanceMonitor.cs` |
| UI ViewModels | `Core/ViewModels/*` |

## 3. 卡顿的根因与对应修复

| 原代码问题 | 现在 |
|---|---|
| Razer 回调里 `Dispatcher.Invoke` 同步等 UI 线程 | 回调只往 `FrameSlot` 拷贝 4 个颜色，零分配、零阻塞 |
| 每帧为每颗灯 new `int[3]`、new `List<int[]>` | 固定 `Rgb[]` 缓冲区复用，渲染路径无分配 |
| 预览每次重建 `LinearGradientBrush`（2×N 个 GradientStop） | 1 像素高的 `WriteableBitmap` 直接写像素，20fps 节流 |
| `load()` 里 2 秒超时的 HTTP 跑在 UI 线程 | 全部走线程池，UI 不等 |
| 每次打开设置窗都重复 `PropertyChanged +=`，永不移除 | 设备 VM 只订阅一次，删除时 `Detach`；设置窗已合并进主窗口 |
| 退出时 `Thread.Sleep(1000)` | 无休眠；停止时先发黑+关 live，再释放 UDP 并 join 渲染线程 |
| 发送和预览同一节奏 | 输出帧率（30–120）与预览帧率（5–60）分开配置 |

## 4. 功能变更

**Chroma 通道 4–32**
- 4/8/12/16/24/32 可选；4 个 Razer 原生颜色（ChromaLink2–5）永远是锚点，
  位置固定 0、1/3、2/3、1。
- ChromaLink1 虽然 SDK 里有，但实测始终是黑色（Synapse 不驱动这个区），
  接进来等于废掉一个锚点，所以不使用。
- 旧配置里的 `Led1..Led4` 映射到 C1..C4，点亮的灯带区域与旧版一致。
- 多出来的通道在三个锚点区间内均分，并在 UI 上标记为 `virtual`（紫色小字），
  原生为 `Razer`。不会把插值颜色冒充成 Razer 实时数据。
- 每台设备、每个 Segment 都能单独指定通道范围（From/To），互不影响。

**WLED 大量灯珠**
- ≤490 颗走 DRGB 单包，>490 颗自动切 DNRGB，每包 490 颗、带绝对起始索引。
- 2000 颗 = 5 包，实测回解码逐字节一致（见 TEST-REPORT.md）。
- 每台设备的物理窗口（LED window）、亮度、渐变、方向、Segment 通道范围独立。

**Segment 修复**
- 原版按段顺序把颜色首尾相接拼成一整条，段起点、段间空洞、`rev` 全部丢失。
- 现在按绝对灯号写入，段间空隙保持黑色，`rev` 单独生效。

**界面**
- 主窗口 1100×760，可自由缩放，左侧导航五项（Overview / Devices / Mapping /
  Performance / Settings），原来的独立设置窗口已并入 Settings 页。
- 深色 + Razer 绿（#44D62C）单一强调色；紫色只用于区分“虚拟通道”。
- 无渐变、无发光、无动画循环；数字统一等宽字体。彩色只出现在色块和灯带预览里。

**交互**
- 关闭窗口 = 收进托盘（首次会提示一次），退出在托盘右键里，且会二次确认。
- 设备开关、亮度、通道范围改动即时生效并自动存盘（250ms 防抖）。
- 掉线设备每 30 秒静默重连一次；重新启用设备会立即触发一次刷新。
- 设置页可 mDNS 搜索（只采纳真正返回 `/json` 的设备）或直接输入 IP 添加。

## 5. 构建与运行

需要：Visual Studio 2022（17.8+）或 .NET SDK 8.0，Windows 10/11。

```
dotnet restore "Razer Chroma WLED Connect App.sln"
dotnet build "Razer Chroma WLED Connect App.sln" -c Release
```

运行需要 **.NET 8 Desktop Runtime**（原版是 .NET 5，两者不通用）。
目标框架从 `net5.0-windows` 升到 `net8.0-windows`，因为 .NET 5 已停止支持。

`settings.xml` 放在 exe 同目录，首次运行会自动读取旧文件，无需手工迁移。

另外修了一个会让别人无法编译的问题：原 `.csproj` 里有两条硬编码的
`C:\Users\Rick\.nuget\...\hidapi64.dll`，现在改为从 NuGet 包的路径属性复制。

## 6. 已知限制

- WPF 界面层没能在我的环境里编译（沙箱没有 Windows 桌面目标包），
  只做了良构性、资源键、绑定路径、类名四项静态校验；请在你的机器上编译一次。
- 预览数据每条设备每次刷新会复制一份数组（20fps × 灯数 × 3 字节），
  输出路径是零分配的。这是为了预览与输出线程解耦做的取舍。
- 灯光输出依赖 WLED ≥ 0.11 的 UDP 实时接口（与原版一致）。

## 7. 第二轮反馈修复

**闪退（主因已定位）**
`Device.Load()` 跑在线程池上，会改设备属性；`Segments` 集合因此在线程池线程里被
`Clear/Add`，而界面的 ItemsControl 正在枚举它 → `InvalidOperationException` 直接终止进程。
点 "Refresh"、插拔设备、自动重连都会触发，所以表现为"多点几下就闪退"。

修复：Core 增加 `IUiDispatcher` 抽象，`DeviceViewModel` 收到跨线程属性变更时先 Post 回
UI 线程再处理集合；`App` 增加 `DispatcherUnhandledException` / `UnhandledException`
兜底，把异常写进程序目录的 `crash.log` 而不是直接退出。

**只允许单实例**
`App.OnStartup` 用命名 Mutex 判定，已有实例时通过命名事件唤醒它（把窗口从托盘叫出来）然后
当前进程退出。避免两个进程抢同一个 Razer App ID。

**同步状态误报 "waiting for Razer"**
Synapse 有时只推颜色、不发 `BroadcastStatus.Live`。现在收到任何一帧真实颜色即置为 Live，
超过 5 秒没有新帧再回落为 Waiting。

**设备发现合并进 Devices 页**
- Devices 页顶部新增 "Find controllers"：搜索 / 停止 / 结果列表。
- 发现到的设备只列出来，必须点 Add 才加入；Ignore 直接丢弃。不再自动添加。
- 同时提供手动输入框，校验 IPv4/IPv6，非法或重复会在下方红字说明原因。
- Settings 页只保留 App ID、通道数、帧率、开机启动、关于。

**调色板改成一条色条**
原来是每通道一个色块（32 通道时满屏都是框）。现在是一条连续的 ColorBar：
- 颜色条本体 = 当前调色板插值结果；
- 下方每个通道一个刻度 + 编号，通道数选 4 就只显示 4 个刻度；
- Mapping 页每台设备各有一条，用高亮带标出这台设备实际使用的那一段（C1–C4 等）。

**原生颜色上限**
`RzChromaBroadcastEffect` 暴露 `ChromaLink1..5`，但只有 2~5 是 Synapse 真正驱动的。
更多通道只能通过插值生成，并在界面上标记为 virtual。

**亮度真的能调了**
WLED 在实时模式下经常忽略状态包里的 `bri`，所以亮度改为在发送前对颜色做软件缩放，
滑块一动下一帧就变暗/变亮。

**Synapse 显示已连接、应用仍显示 Waiting**
两个原因都修了：一是状态回调用 Razer 自己的线程触发，跨线程更新绑定属性会被静默丢弃，
现在统一 Post 回 UI 线程；二是原来"5 秒没收到帧就回落"太激进——Synapse 只在颜色变化时
推送，空闲几秒很正常，现已放宽到 60 秒。
