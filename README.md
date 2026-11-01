# Voice Input Buddy (Windows)

**Voice Input Buddy（语音搭子）Windows 版** 把蓝牙电视遥控器变成 Windows 电脑上的全局语音输入按钮。拿起遥控器，按住说话、松开结束，即可在当前任意输入框中唤起输入法（微信输入法、豆包输入法等）或系统听写工具进行流畅输入。

本项目基于 **原生 Windows 技术栈（C# / .NET 8）** 构建，坚持**纯软件方案**，直接利用电脑内置蓝牙连接遥控器，无需额外购买或插拔任何 USB 硬件。

---

## 🌟 核心特性

- **零额外硬件**：直接通过 Windows 自带低功耗蓝牙（BLE）连接小米遥控器、Google Chromecast 遥控器等设备；
- **原生极速流转**：基于 ATVV 协议实时接收语音，纯 C# 内存级解码 IMA ADPCM（16kHz 16-bit PCM），经由低延迟 WASAPI 写入虚拟麦克风；
- **智能快捷键注入**：支持“按住说话（Hold-to-Talk）”与“点按切换（Toggle）”，无缝对接微信输入法、豆包输入法以及 Windows 11 自带语音听写（`Win + H`）；
- **全按键自定义映射**：支持将遥控器的方向键、确认键、返回键等映射为常用快捷键、窗口切换操作或 Unicode 常用语快速上屏；
- **极简一体化体验**：四步式新手设置向导，内置驱动自动化检测与一键修复，无需繁琐的手工配置；
- **严格隐私保护**：应用纯本地运行，不保存录音历史，不统计按键日志，无远程遥测。

---

## 🏗️ 架构与工作流

```text
遥控器麦克风 → BLE GATT 语音流 → Voice Input Buddy (C# 解码) → WASAPI → 虚拟麦克风 (VB-CABLE) → 输入法
遥控器语音键 → Raw Input 监听   → Voice Input Buddy 状态机    → SendInput → 触发目标语音快捷键   → 当前输入框
```

---

## 📚 设计与开发文档

相关技术细节与规范已在 `docs/` 目录下完成详细归档：

- 🏛️ [软件架构设计 (Architecture)](docs/architecture.md)：技术栈选型、分层设计与线程并发模型；
- 📡 [遥控器与 ATVV 协议规范 (Device Protocols)](docs/device-protocols.md)：支持设备列表、ATVV v1.0 握手报文、IMA ADPCM 解码算法与 HID Usage 码表；
- 🎙️ [虚拟音频与驱动集成方案 (Virtual Audio & Driver)](docs/virtual-audio-and-driver.md)：Windows 驱动签名应对策略、VB-CABLE 集成与 WASAPI 低延迟推流引擎；
- ⌨️ [按键监听与快捷键注入规范 (Input & Hotkeys)](docs/input-and-hotkeys.md)：Raw Input 捕获、`SendInput` 时序保证及常用语 Unicode 上屏；
- 🗺️ [开发路线图与里程碑 (Development Roadmap)](docs/development-roadmap.md)：从 CLI 原型验证到完整 UI 发布的分阶段研发计划。

---

## 🛠️ 技术栈总览

| 模块 | 技术选型 |
| :--- | :--- |
| **运行时** | .NET 8 (C# 12) |
| **UI 框架** | WPF (配合 [lepoco/wpfui](https://github.com/lepoco/wpfui) 现代化控件库) / WinUI 3 |
| **蓝牙通信** | Windows 10/11 原生 WinRT BLE API (`Windows.Devices.Bluetooth`) |
| **音频处理** | [NAudio](https://github.com/naudio/NAudio) (WASAPI Render 模式) + 纯 C# IMA ADPCM 解码器 |
| **虚拟麦克风** | [VB-CABLE](https://vb-audio.com/Cable/) (已获微软 WHQL 认证驱动) |
| **输入注入** | Win32 API (`SendInput` + `RegisterRawInputDevices`) |
| **打包分发** | Inno Setup 一体化自解压安装包 |

---

## 📄 开源许可

本项目遵循 [GPL-3.0-only](LICENSE) 协议发布。
