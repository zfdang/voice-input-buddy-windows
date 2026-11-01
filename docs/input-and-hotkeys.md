# 按键监听与快捷键注入规范 (Input & Hotkeys)

本文档阐述 Windows 平台下遥控器全局按键的捕获方案、输入法语音快捷键的注入时序，以及普通按键映射与常用语上屏的实现。

---

## 一、按键事件捕获方案

电视遥控器在与 Windows 配对后，通常以 **HID 键盘 + 消费控制设备（Consumer Control Device）** 的形式挂载在系统中。

为了在后台无焦点、全屏游戏或任何应用处于活动状态时都能可靠捕获遥控器按键，采用 **Windows Raw Input API** 与 **低级键盘钩子（Low-Level Keyboard Hook）** 双结合方案：

### 1. Raw Input 注册 (HID Consumer 页面)
通过 `RegisterRawInputDevices` 监听 Consumer Control 页面（Page `0x0C`）：
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct RAWINPUTDEVICE
{
    public ushort usUsagePage;
    public ushort usUsage;
    public uint dwFlags;
    public IntPtr hwndTarget;
}

public void RegisterConsumerDevice(IntPtr hwnd)
{
    RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
    rid[0].usUsagePage = 0x0C;      // Consumer Control
    rid[0].usUsage = 0x01;          // Consumer Control Device
    rid[0].dwFlags = 0x00000100;    // RIDEV_INPUTSINK (即使无焦点也接收)
    rid[0].hwndTarget = hwnd;

    RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
}
```

### 2. 语音键长按与松开防抖 (Hold-to-Talk)
* **按下事件（Key Down）**：
  * 标记语音会话激活；
  * 向系统注入目标输入法的“开始语音”快捷键；
  * 向遥控器下发 ATVV `MicrophoneOpen` 指令。
* **松开事件（Key Up）**：
  * 向系统注入“结束语音”快捷键；
  * 向遥控器下发 ATVV `MicrophoneClose` 指令。
* **蓝牙丢包看门狗（Safety Watchdog）**：
  * 无线遥控器偶发可能丢失 Key Up 报文。程序内置 60 秒硬超时看门狗，若超过最长单次语音时长未收到心跳或按键变化，自动安全释放会话，防止输入法无限期录音。

---

## 二、快捷键模拟与输入法联动 (`SendInput`)

Windows 系统下模拟按键最底层、兼容性最好的 API 是 Win32 `SendInput`。它能保证注入的键位被当前前台活动窗口（微信、Word、浏览器、IDE）正确识别。

### 1. 结构体定义 (C#)
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct INPUT
{
    public uint type;
    public InputUnion u;
}

[StructLayout(LayoutKind.Explicit)]
public struct InputUnion
{
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

public const uint INPUT_KEYBOARD = 1;
public const uint KEYEVENTF_KEYUP = 0x0002;
public const uint KEYEVENTF_UNICODE = 0x0004;
```

### 2. 主流输入法联动矩阵

| 目标输入工具 | 推荐触发方式 | 快捷键配置 | 行为模式 |
| :--- | :--- | :--- | :--- |
| **微信输入法 (Windows)** | 自定义组合键 | 如 `Ctrl + Shift + Alt + V` | **按住说话**（按下发 KeyDown，松开发 KeyUp）或 **单击切换** |
| **豆包输入法 (Windows)** | 独立快捷键 | 如 `F1` 或 自定义组合键 | 支持按住说话与单击开始/结束 |
| **Windows 11 自带语音听写** | 系统原生内置 | `Win + H` | 系统自带，按下 `Win + H` 启动听写，再次按下或静音自动提交 |
| **Typeless / VoiceCursor** | 专用快捷键 | 自定义 | 配合虚拟声卡与专属触发热键 |

### 3. 组合键注入时序保证
为防止操作系统因为按键顺序颠倒误判快捷键，组合键注入必须严格按照 **先按下修饰键 -> 再按下主键 -> 释放主键 -> 释放修饰键** 的顺序：
```csharp
public static void SendModifiedKey(ushort modifierVk, ushort keyVk)
{
    var inputs = new INPUT[]
    {
        // 1. 按下修饰键 (如 Ctrl)
        CreateKeyInput(modifierVk, false),
        // 2. 按下主键 (如 V)
        CreateKeyInput(keyVk, false),
        // 3. 释放主键
        CreateKeyInput(keyVk, true),
        // 4. 释放修饰键
        CreateKeyInput(modifierVk, true)
    };
    SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
}
```

---

## 三、普通按键映射与常用语上屏

遥控器的其他按键（如方向、确认、返回、主页、音量）可在设置中灵活映射：

### 1. 动作类型支持
1. **多媒体控制**：音量增加/减少、静音、播放/暂停、下一曲；
2. **快捷键宏**：映射为任意单键或复杂组合键（如 `Ctrl + C`、`Enter`、`Alt + Tab`）；
3. **常用语直接输入 (Text Snippet)**：一键输入预设文本（如常用邮箱、家庭地址、固定回复）。

### 2. 常用语上屏技术实现 (Unicode 注入)
常用语不需要模拟对应字符的物理键盘打字，直接使用 `KEYEVENTF_UNICODE` 注入 Unicode 字符流，中英文混写零乱码、直接上屏：
```csharp
public static void SendText(string text)
{
    var inputs = new List<INPUT>();
    foreach (char c in text)
    {
        // 按下 Unicode 字符
        inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE } }
        });
        // 释放 Unicode 字符
        inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } }
        });
    }
    SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
}
```
