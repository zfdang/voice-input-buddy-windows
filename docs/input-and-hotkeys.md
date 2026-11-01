# 按键监听与快捷键注入规范 (Input & Hotkeys)

本文档阐述 Windows 平台下遥控器按键的捕获层级限制、输入法端到端时序保证（首尾音防护）、SendInput 64位结构对齐与 UIPI 权限边界。

---

## 一、按键事件处理层级与系统限制

在 Windows 系统中，捕获和拦截外部 HID 设备的按键存在明确的系统级机制限制。必须清晰区分以下三个层级：

```text
┌─────────────────┐       ┌─────────────────┐       ┌─────────────────┐
│     Level 1     │       │     Level 2     │       │     Level 3     │
│   全局监听 (Sink)│  ──>  │   附加映射 (Add) │  ──>  │   替换屏蔽 (Block)│
└─────────────────┘       └─────────────────┘       └─────────────────┘
 Raw Input (RIDEV)        触发自定动作 + 原始动作   彻底吞掉原始动作 (PoC目标)
 (可区分遥控器设备)       (如音量键打字且调音量)     (低级钩子无法区分设备)
```

### 1. Level 1: 全局监听 (Passive Monitoring)
* **技术实现**：通过 Win32 [Raw Input API](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputdevice) 注册 Consumer Control 页面（Page `0x0C`），启用 `RIDEV_INPUTSINK` 标志。
* **特性**：即使当前窗口无焦点，后台也能稳定捕获事件；`RAWINPUT.header.hDevice` 能清晰区分按键是来自遥控器还是主键盘。
* **边界**：**Raw Input 纯粹作为监听者（Sink），绝不能阻止原始按键继续传递给系统！**

### 2. Level 2: 附加映射 (Additive Mapping)
* **行为**：捕获到遥控器按键后，触发用户配置的快捷键或文本注入，**但原始系统按键动作仍然会发生**。
* **示例**：若将遥控器“音量加”键映射为常用语，按下时会在屏幕上输入文字，**同时系统的音量条也会增加**。

### 3. Level 3: 替换原始动作 (Exclusive Replacement / Blocking) —— **Phase 1 验证目标**
* **难点与矛盾**：
  * 若要彻底“吞掉（Block）”原始按键，传统 Windows API 需要使用低级键盘钩子 `SetWindowsHookEx(WH_KEYBOARD_LL)`；
  * 但是，[KBDLLHOOKSTRUCT 结构体](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct) **根本不包含设备句柄 `hDevice`**！
  * 如果在全局钩子中直接拦截并返回 1，会导致用户电脑自带的物理键盘对应按键也被误拦截。
* **决策**：**在 Phase 1 阶段，将“区分遥控器设备句柄并彻底屏蔽原始动作”列为 PoC 预研与验证目标**，在初期版本中默认仅承诺 Level 2 附加映射，待真机验证无副作用后再开放独占替换。

---

## 二、端到端时序保证：首音防截断与尾音防剪切

输入法与语音遥控器的联动绝非“按下发快捷键、松开立刻发结束”这么简单。由于蓝牙传输延迟与音频缓冲，时序必须严格编排：

```text
【语音启动流程 (Hold to Talk - Start)】
1. 用户按下遥控器语音键 (或遥控器上报 0x08 START_SEARCH)
2. 开启音频管线 Command Pre-roll（首音预存缓冲区，约 50~100ms）
3. 调用 SendInput 向前台注入输入法开始录音快捷键
4. 向遥控器下发 0x0C (MIC_OPEN) -> 接收 0x04 (AUDIO_START) 并提取 SessionID
5. 将 Pre-roll 缓冲与后续流数据无缝送入 WASAPI 输出

【语音结束流程 (Hold to Talk - Stop)】
1. 用户松开语音键
2. 【禁止立即停用输入法！】先向下发 0x0D (MIC_CLOSE, SessionID)
3. 持续接收 BLE 传输中的残余音频包，直到收到 0x00 (AUDIO_STOP) 或尾包超时
4. 最后一批 PCM 数据送入 WASAPI 缓冲区
5. 等待虚拟音频队列排空 (Drain，约 40~60ms)
6. 【此时才向系统注入输入法结束录音快捷键】
```

---

## 三、`SendInput` 64 位结构对齐与 UIPI 权限边界

在 64 位 Windows 操作系统下，Win32 `INPUT` 结构体的大小与内存布局受 C++ `union` 最大成员制约。

### 1. 结构体大小对齐（x64 下严格为 40 字节）
在 64 位平台下，`MOUSEINPUT` 由于包含 64 位指针 `ULONG_PTR dwExtraInfo`，尺寸为 28 字节，向上 8 字节对齐后占据 32 字节。加上 `DWORD type`（4 字节 + 4 字节填充），整个 `INPUT` 结构体在 x64 下严格为 **40 字节**（32 位下为 28 字节）。
* 若 union 仅定义 16 字节的 `KEYBDINPUT`，会导致计算出的结构体尺寸错误，直接导致 `SendInput` 调用失败并报 `ERROR_INVALID_PARAMETER (87)`。

### 2. 正确完整的 C# P/Invoke 声明
```csharp
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct INPUT
{
    public uint type;
    public InputUnion u;
}

[StructLayout(LayoutKind.Explicit)]
public struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
    [FieldOffset(0)] public HARDWAREINPUT hi;
}

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public UIntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public UIntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct HARDWAREINPUT
{
    public uint uMsg;
    public ushort wParamL;
    public ushort wParamH;
}

public static class NativeInputSender
{
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    public static bool SendModifiedKey(ushort modifierVk, ushort keyVk)
    {
        var inputs = new INPUT[4];
        inputs[0] = CreateKey(modifierVk, false);
        inputs[1] = CreateKey(keyVk, false);
        inputs[2] = CreateKey(keyVk, true);
        inputs[3] = CreateKey(modifierVk, true);

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    private static INPUT CreateKey(ushort vk, bool isKeyUp) => new INPUT
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = isKeyUp ? KEYEVENTF_KEYUP : 0
            }
        }
    };
}
```

### 3. UIPI (User Interface Privilege Isolation) 权限约束
* **系统限制**：根据 Windows 安全架构，低完整性级别（普通权限）的进程向高完整性级别（以“管理员身份运行”）的窗口发送按键注入时，将被系统 UIPI 静默丢弃，`SendInput` 会返回 0 且失败；
* **应用设计说明**：
  * 本应用在普通权限下运行时，无法向以管理员身份运行的命令提示符、任务管理器或特权游戏注入快捷键；
  * 向导中应明确告知用户此项系统限制，或在需要时支持以提升权限重启。
