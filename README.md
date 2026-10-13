# ShakeFH2

为通过 **Azimuth** 运行的 Xbox One 版《极限竞速：地平线 2》（Forza Horizon 2）及《地平线 2：速度与激情》（Forza Horizon 2 Presents Fast & Furious，常称“速度与激情 DLC”）恢复 Xbox 手柄的扳机震动。

作者：**栗尾玲子KurioReiko**

工具读取游戏原本提交的左右握把、左右扳机四路震动强度，并在游戏进程内通过 Windows.Gaming.Input 转发到手柄。扳机反馈使用游戏原生数据，保留握把震动。

**支持 USB 有线和蓝牙连接。** 蓝牙连接需要使用具备蓝牙功能及扳机震动马达的 Xbox 手柄。

## 适用版本

| 项目 | 适用环境 |
| --- | --- |
| 运行器 | **Azimuth 0.9.7，Windows x64** |
| 游戏 | **Xbox One 版《地平线 2》（Anthem 1.0.0.64）及《地平线 2：速度与激情》** |
| 系统 | Windows 11 x64，系统自带 .NET Framework 4.x |
| 手柄 | Xbox 手柄 |

**当前实测记录为 Azimuth 0.9.7 +《地平线 2》Anthem 1.0.0.64 + USB Xbox 手柄。新版本的 Azimuth、新版本的游戏，以及其他构建不保证支持。** 本工具依赖特定的游戏运行器函数特征；更新后可能无法定位，或者原有行为已经变化。请不要把“能启动工具”当成版本兼容的证明。

Xbox One / Series 手柄需要有左右扳机震动马达。目前保存的实测记录为 USB 连接；蓝牙连接受手柄固件和 Windows 驱动影响，尚未记录蓝牙实测结果。Xbox 无线接收器、多手柄和虚拟手柄组合尚未实测。不适用于 Xbox 360 版《地平线 2》，也不是《地平线 3 / 4 / 5》的插件。

## 下载和使用

1. 从 [Releases](https://github.com/Kur1oR3iko/ShakeFH2/releases) 下载 Windows x64 压缩包，解压到独立文件夹。
2. 保持 `ShakeFH2.exe` 和 `XboxTriggerOutput.dll` 在同一个文件夹。
3. 通过 USB 或蓝牙连接 Xbox 手柄，运行 `ShakeFH2.exe`。
4. 使用你原来的方式启动游戏。窗口会显示灰色“游戏：未启动”或绿色“游戏：已启动”；工具自动连接正在运行的 Azimuth。
5. 保持工具运行，进入驾驶场景测试刹车和加速打滑等反馈。

无须把 DLL 手动放进游戏目录，也无须安装 Python、Zig 或额外的运行库。工具不包含游戏文件、运行器、DLC、存档或其他游戏插件。

分发包仅包含 `ShakeFH2.exe`、`XboxTriggerOutput.dll` 和简要 `README.md`。完整说明、源码、验证记录及第三方许可文件保留在仓库中。

通常不需要管理员权限。如果游戏以管理员身份运行，工具需要以同等权限运行才能连接。

## 窗口功能

- **启用扳机桥接**：开启四路输出；取消后恢复原来的游戏震动处理。
- **扳机强度**：仅缩放左右扳机，默认 100%；握把强度保持游戏原值。
- **手柄编号**：选择 Windows.Gaming.Input 枚举中的手柄，默认第 1 个。
- **实时震动**：显示最新采样的四路震动强度。
- **近 3 秒震动峰值**：保留短脉冲的最大值，方便观察。仅影响显示。
- **测试扳机**：游戏未运行时依次短暂测试左扳机、右扳机和握把。
- **停止并退出**：停止工具并释放震动输出接管。

窗口数值表示**马达震动强度，不是扳机按下的深度**。只按下扳机，而游戏没有产生反馈时，数值仍可能为零。

## 工作方式与限制

工具通过特征码定位 Azimuth 的 `SetVibration`，在运行中的进程内捕获游戏提交的四个 `float`。`XboxTriggerOutput.dll` 在游戏进程内使用 `Gamepad.Vibration` 驱动四路马达，以避免本机独立后台程序“接口成功但实际不震动”的问题。

函数特征不匹配、匹配结果不唯一或被其他工具改写时，工具拒绝安装新的入口补丁。不要同时使用其他修改同一震动函数的工具。桥接就绪后暂时接管原有 XInput 输出，避免两套输出互相覆盖。

输出线程约每 8 毫秒发送最新值，可能增加延迟或漏过更短的变化；超过 750 毫秒没有新信号时清零。强度为 100% 时，合法范围内的原生强度数值不变。**未与 Xbox 主机做同步测量，不保证最终触感和时序完全一致。**

工具使用共享心跳和调用租约，在异常停止后尝试停止输出并恢复原处理函数。进程内模块和被动跳板保留到游戏退出，避免其他线程正在执行时被移除。若线程或驱动故障导致残留震动，可退出游戏或重连手柄。

工具不改写游戏可执行文件、画面插件或存档。运行日志保存在工具目录的 `bridge.log`；报告问题时可提供运行器版本、游戏版本、手柄型号、连接方式和相关日志。

## 从源码编译

需要 Windows x64、系统 .NET Framework 4.x 的 C# 编译器，以及 Zig。原生模块已使用 **Zig 0.16.0** 构建验证。Zig 仅用于开发，最终用户不需要安装。

```powershell
# Zig 已在 PATH 中
.\build.ps1

# 或显式指定编译器
.\build.ps1 -ZigPath 'C:\tools\zig\zig.exe'
```

成品输出到 `dist\ShakeFH2`。游戏运行时不要覆盖它已加载的 DLL。

```powershell
# 验证内存桥接和回退逻辑，不需要启动游戏
& .\dist\ShakeFH2\ShakeFH2.exe --self-test | Out-String

# 仅读取手柄枚举和状态
& .\dist\ShakeFH2\ShakeFH2.exe --probe | Out-String

# 游戏关闭时，依次短暂测试各路马达
& .\dist\ShakeFH2\ShakeFH2.exe --test-motors | Out-String
```

## 验证

七项模拟进程测试通过，当前 USB Xbox 手柄在实际驾驶场景中确认扳机与握把震动正常。详见 [VALIDATION.md](VALIDATION.md)。

## 参考与署名

四路 float 布局及 Azimuth 的函数特征定位参考 [FH2-DualSense](https://github.com/aminenrr/FH2-DualSense) 的 `app/azimuth_hook.py`（MIT）。Xbox 输出模块、调用租约、线程检查、界面及测试独立实现。第三方许可见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。

Windows.Gaming.Input ABI 使用 Microsoft Windows SDK 的公开接口定义。本工具与 Microsoft、Playground Games 或 Azimuth 开发者没有官方关联。
