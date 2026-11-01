# 遥控器与 ATVV 协议规范 (Device Protocols)

本文档整理了 Windows 平台支持的蓝牙电视遥控器规格、ATVV（Android TV Voice）协议细节、音频编解码算法以及 HID 按键报文映射。

---

## 一、支持的硬件设备范围

| 硬件型号 | 识别特征 (VID/PID / 名称) | 语音协议 | 普通按键支持 | 状态与说明 |
| :--- | :--- | :--- | :--- | :--- |
| **小米蓝牙遥控器 2** (RC001) | 蓝牙广播名包含 `XiaoMi` 或 `RC001` | ATVV v1.0 (16kHz) | 支持（完整方向、确认、主页、返回、音量） | 已验证协议，默认高位在前 (High Nibble First) |
| **小米蓝牙遥控器 2 Pro** (RC003) | 蓝牙广播名包含 `RC003` | ATVV v1.0 (16kHz) | 支持 | 已验证协议，高位在前 |
| **Google Chromecast 遥控器** (G9N9N) | VID: `0x18D1`, PID: `0x9450`；名称 `Chromecast Remote` | ATVV v1.0 (16kHz) | 支持（通过 Consumer Control Report 1/2） | 语音与按键均已通过公开报文验证 |
| **Google TV Streamer 遥控器** (GY3LE) | Google 2024 款新品 | ATVV 兼容 | 计划支持 | 待真机验收 |

---

## 二、ATVV (Android TV Voice over BLE) 协议规范

ATVV 是 Google Android TV 生态通用的低功耗蓝牙语音传输协议，小米电视遥控器亦深度兼容此协议。

### 1. GATT 服务与特征值定义

所有相关特征值均挂载在主服务 `AB5E0001` 之下：

| 角色 / 特征值 | UUID | 属性 (Properties) | 作用 |
| :--- | :--- | :--- | :--- |
| **主服务 (Service)** | `AB5E0001-5A21-4F05-BC7D-AF01F617B664` | Primary Service | 语音服务入口 |
| **写入特征 (Transmit)** | `AB5E0002-5A21-4F05-BC7D-AF01F617B664` | Write / Write Without Response | 主机向遥控器发送控制命令（开麦/关麦/能力协商） |
| **音频流特征 (Audio)** | `AB5E0003-5A21-4F05-BC7D-AF01F617B664` | Notify | 遥控器向主机持续上报分包的 ADPCM 音频流 |
| **控制/响应特征 (Control)** | `AB5E0004-5A21-4F05-BC7D-AF01F617B664` | Notify / Indicate | 遥控器向主机上报能力响应、按键状态等控制报文 |

### 2. 语音会话状态流转

```text
[主机 App]                                        [遥控器]
    │                                                │
    │ 1. 写入 GetCapabilities (0x0A, 0x01, ...)       │
    ├───────────────────────────────────────────────>│
    │ 2. Control 特征返回 Capabilities (0x0B, ...)    │
    │<───────────────────────────────────────────────┤ (协商采样率 16kHz, Codec: ADPCM)
    │                                                │
    │ 3. [用户按下语音键] 写入 MicOpen (0x0C, 0x00)     │
    ├───────────────────────────────────────────────>│
    │ 4. Audio 特征持续上报分包语音数据 (ADPCM)        │
    │<───────────────────────────────────────────────┤ (每包约 20~100 字节)
    │<───────────────────────────────────────────────┤
    │                                                │
    │ 5. [用户松开语音键] 写入 MicClose (0x0D, ...)    │
    ├───────────────────────────────────────────────>│
    │ 6. 语音流停止                                   │
```

### 3. 指令二进制格式

* **GetCapabilities (查询能力)**：
  ```csharp
  byte[] GetCapabilitiesV10 = new byte[] { 0x0A, 0x01, 0x00, 0x00, 0x03, 0x03 };
  ```
  遥控器在 `Control` 特征值返回以 `0x0B` 开头的字节数组，包含支持的编码格式、交互模式及每帧数据量。

* **MicrophoneOpen (开启麦克风)**：
  ```csharp
  // ATVV v1.0 及以上版本
  byte[] MicOpenCmd = new byte[] { 0x0C, 0x00 };
  ```

* **MicrophoneClose (关闭麦克风)**：
  ```csharp
  // 传入当前 sessionID (通常为 0)
  byte[] MicCloseCmd = new byte[] { 0x0D, sessionID };
  ```

* **MicrophoneExtend (会话保活 / 延长)**：
  ```csharp
  // 长按说话时可定期下发保活，防止遥控器自主超时关麦
  byte[] MicExtendCmd = new byte[] { 0x0E, sessionID };
  ```

---

## 三、IMA ADPCM 解码算法规范

遥控器麦克风采集的 16kHz 音频被压缩为 4-bit IMA ADPCM 格式传输（数据压缩比 4:1，1 字节包含 2 个采样点）。

### 1. 算法常数表
```csharp
public static class ImaAdpcmTables
{
    public static readonly int[] StepTable = new int[]
    {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
        34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130,
        143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449,
        494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411,
        1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026,
        4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442,
        11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623,
        27086, 29794, 32767
    };

    public static readonly int[] IndexTable = new int[]
    {
        -1, -1, -1, -1, 2, 4, 6, 8
    };
}
```

### 2. Nibble 顺序与固件适配
* **小米 RC001 / RC003**：默认采用 **高半字节优先 (High Nibble First)**。即一个字节中，`(byte >> 4) & 0x0F` 为第 1 个采样，`byte & 0x0F` 为第 2 个采样；
* **部分第三方 / 兼容固件（如 ARN9）**：采用 **低半字节优先 (Low Nibble First)**。软件应提供解码配置项。

### 3. 解码单步逻辑 (C# 示范)
```csharp
public short DecodeSample(byte nibble)
{
    int step = ImaAdpcmTables.StepTable[_stepIndex];
    int diff = step >> 3;

    if ((nibble & 4) != 0) diff += step;
    if ((nibble & 2) != 0) diff += step >> 1;
    if ((nibble & 1) != 0) diff += step >> 2;

    if ((nibble & 8) != 0)
        _predictor -= diff;
    else
        _predictor += diff;

    // 限制在 16-bit 有符号数范围内
    _predictor = Math.Clamp(_predictor, -32768, 32767);

    // 更新索引
    _stepIndex += ImaAdpcmTables.IndexTable[nibble & 7];
    _stepIndex = Math.Clamp(_stepIndex, 0, 88);

    return (short)_predictor;
}
```

---

## 四、HID 按键报文映射

除语音外，遥控器的普通按键通过标准 HID 报文上报（Consumer Control Page `0x0C`）：

### 1. Google Chromecast 遥控器 (G9N9N)
G9N9N 遥控器在 Windows 系统中会以标准 BLE HID 键盘/多媒体设备挂载。

* **Report ID 1 (索引式报文)** 与 **Report ID 2 (直接 Usage 报文)** 对应表：
  * 方向上 (Up)：`0x0042`
  * 方向下 (Down)：`0x0043`
  * 方向左 (Left)：`0x0044`
  * 方向右 (Right)：`0x0045`
  * 确认 (OK / Select)：`0x0041`
  * 返回 (Back)：`0x0224`
  * 主页 (Home)：`0x0223`
  * 静音 (Mute)：`0x00E2`
  * 音量+ (Vol Up)：`0x00E9`
  * 音量- (Vol Down)：`0x00EA`
  * YouTube：`0x0077`
  * Netflix：`0x0078`
  * 信号源 (Input)：`0x0189` / `0x0089`
  * 电源 (Power)：`0x019E`

### 2. 小米遥控器按键映射
小米遥控器同样走标准 Consumer Control 报文，其语音按键在不同固件下表现为专用的按键码或直接在 ATVV 层面触发会话。
Windows 端通过 Windows Raw Input API（`RegisterRawInputDevices`）进行全局监听，无需获取特定窗口焦点即可拦截按键。
