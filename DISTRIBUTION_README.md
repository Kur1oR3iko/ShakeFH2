# ShakeFH2 v2.0.0

作者：**栗尾玲子KurioReiko**

一个软件，两个界面：手柄震动和键盘映射。支持手柄与键盘同时输入；切换页面只改变显示，两个功能独立启用。

适用 **Azimuth 0.9.7（Windows x64）**，支持 Xbox One 版《地平线 2》及《地平线 2：速度与激情》。实测游戏为 Anthem 1.0.0.64。**新版本运行器、游戏及其他构建不保证支持。**

## 使用

1. EXE 与 XboxTriggerOutput.dll 放在同一目录，运行 EXE。升级前退出旧版工具和游戏。
2. 保留旧键位时，复制旧 KeyFH2.ini。
3. 按原有方式启动游戏；手柄支持 USB 或蓝牙，无须为使用键盘断开手柄。
4. 震动页调强度，数值自动保存在 `ShakeFH2.ini`；键盘页双击改键位，自动保存在 `KeyFH2.ini`。F8 暂停键盘映射，手柄仍可操作。
5. 关闭软件释放输入和震动接管。

键盘需要已安装 ViGEmBus 驱动，客户端已内置。可能出现额外的同名隐藏输入进程，软件只有一个操作窗口。

默认 W/S 油门刹车、A/D 转向、Space 手刹、Enter 确认、Backspace 返回、Esc 暂停、Q/E 手动换挡、Shift 手动离合、R 回放、C 视角、M 地图，均可修改。

合并第一台可用实体手柄与键盘。按钮合并，扳机取较大值；键盘轴非零时优先，回正后用手柄值。近 3 秒峰值只用于显示，数值不是扳机深度。键盘转向为可调渐进，不复现 FH4 内部算法。

不改写游戏文件或存档，不包含游戏、运行器或 DLC。完整说明：[GitHub](https://github.com/Kur1oR3iko/ShakeFH2)。

## 第三方许可通知

FH2-DualSense: https://github.com/aminenrr/FH2-DualSense
Azimuth vibration signature and four-channel float layout reference.
MIT License
Copyright (c) 2026 aminenrr

ViGEmClient: https://github.com/nefarius/ViGEmClient
Native x64 library from official Nefarius.ViGEm.Client 1.21.256.
MIT License
Copyright (c) 2018 Benjamin Höglinger-Stelzer

The following terms apply separately to each component above:

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
