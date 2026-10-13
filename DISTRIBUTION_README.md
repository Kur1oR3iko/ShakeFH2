# ShakeFH2

作者：**栗尾玲子KurioReiko**

为 Xbox 手柄恢复原生扳机震动，并保留握把反馈。支持 USB 有线和蓝牙连接。

支持游戏：Xbox One 版《地平线 2》及《地平线 2：速度与激情》（常称“速度与激情 DLC”）。

适用运行器：**Azimuth 0.9.7（Windows x64）**。当前实测为《地平线 2》Anthem 1.0.0.64 + USB Xbox 手柄。**新版本的运行器、游戏及其他构建不保证支持。**

## 使用

1. 保持 `ShakeFH2.exe` 和 `XboxTriggerOutput.dll` 在同一文件夹，运行 EXE。
2. 连接 Xbox 手柄，按原有方式启动游戏；工具自动连接。
3. 保持工具运行。关闭工具可恢复原有震动处理。

扳机强度默认 100%。窗口显示震动强度，不是扳机按下的深度；近 3 秒峰值只用于显示。

本工具不包含游戏、运行器或 DLC，不改写游戏文件或存档。完整说明、源码与验证记录：[GitHub](https://github.com/Kur1oR3iko/ShakeFH2)。

## 第三方许可通知

Azimuth 函数特征及四路 float 布局参考 [FH2-DualSense](https://github.com/aminenrr/FH2-DualSense)。

MIT License

Copyright (c) 2026 aminenrr

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
