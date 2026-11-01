# 虚拟音频与驱动集成方案 (Virtual Audio & Driver)

本文档阐述 Windows 平台下虚拟音频驱动的技术约束、VB-CABLE 集成方案以及 C# WASAPI 低延迟音频写入管线的实现细节。

---

## 一、Windows 驱动约束与方案选型

### 1. 为什么不能从零自制虚拟声卡驱动？
在 64 位 Windows 10/11 操作系统中，系统音频端点必须由底层音频驱动（基于 WDM / SysVAD 规范的 `.sys` 文件）创建。
* **微软 DSE（Driver Signature Enforcement）强制限制**：Windows 内核强制要求内核模式驱动必须经过微软官方认证（WHQL 签名或 Hardware Dev Center 交叉签名）；
* **自签名的局限**：自建数字证书或未签名的内核驱动会被 Windows 直接拦截（报错 Code 52），除非开启 Windows“测试模式（Test Mode）”，但开启测试模式会导致右下角水印且防作弊网游无法运行；
* **企业认证成本**：通过正规微软认证需要注册企业账号、购买硬件 Key 保护的 EV 代码签名证书（每年数千元人民币），对个人和轻量开源项目不可行。

### 2. 破局之道：选用经过微软 WHQL 认证的 VB-CABLE
[VB-CABLE](https://vb-audio.com/Cable/)（由 VB-Audio Software 出品）是 Windows 平台上普及度最高、拥有正规微软 WHQL 认证签名的虚拟音频线。

* **工作机制**：
  * **CABLE Input（播放设备）**：作为 Windows 的音频输出端点。我们的 App 负责将遥控器解码后的 PCM 音频实时播放写入此处；
  * **CABLE Output（录音设备 / 麦克风）**：作为 Windows 的音频输入端点。微信输入法、豆包输入法、Windows 听写工具选择该麦克风读取声音。
  * **两者内部直通**：写入 CABLE Input 的声音会被其驱动自动复制送入 CABLE Output，实现无缝环回。

---

## 二、驱动自动化检测与静默安装设计

为了让用户拥有与 macOS 版一样“一键搞定”的极简体验，软件在交付层面做深度封装：

```text
[启动设置向导 / 安装包]
         │
         ▼
[检查注册表 / CoreAudio API 枚举]
是否已存在 "CABLE Input" / "CABLE Output"？
         │
    ┌────┴────┐
   是         否
    │         │
    │         ▼
    │    [提示用户授权 UAC 管理员权限]
    │         │
    │         ▼
    │    [静默调用驱动安装命令]
    │    VBCABLE_Setup_x64.exe -i -h
    │         │
    │         ▼
    │    [监听设备变更 WM_DEVICECHANGE]
    │    等待音频服务加载完成 (约 1~2 秒)
    │         │
    └────┬────┘
         ▼
[驱动就绪，标记向导状态绿色通过]
```

### 1. 驱动存在性检测 (C# 代码示例)
借助 `NAudio.Wave.WasapiOut` 或 `CoreAudioApi.MMDeviceEnumerator` 枚举当前系统的录音与播放设备：

```csharp
using NAudio.CoreAudioApi;

public bool IsVirtualCableInstalled()
{
    var enumerator = new MMDeviceEnumerator();
    var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
    var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

    bool hasInput = renderDevices.Any(d => d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
    bool hasOutput = captureDevices.Any(d => d.FriendlyName.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase));

    return hasInput && hasOutput;
}
```

### 2. 安装包与静默安装命令
在 Inno Setup 安装向导或主程序的修复向导中执行：
```powershell
# 64 位静默安装 (Silent Install)
VBCABLE_Setup_x64.exe -i -h

# 静默卸载 (Silent Uninstall)
VBCABLE_Setup_x64.exe -u -h
```

---

## 三、WASAPI 低延迟推流引擎实现

当遥控器麦克风激活时，解码出的 PCM 数据流必须连续、平滑、低延迟地泵入虚拟声卡。

### 1. 音频格式规格
* **采样率**：`16,000 Hz` (16kHz)
* **声道数**：`1` (Mono 单声道)
* **采样位深**：`16-bit Signed Integer (short)`
* **波形格式**：标准 PCM (`WaveFormat(16000, 16, 1)`)

### 2. 环形缓冲队列 (Jitter Buffer) 设计
蓝牙无线传输由于受到射频干扰或系统调度影响，数据包的上报间隔可能存在微小抖动（Jitter）。
* 使用一个容量为 **200ms（约 3200 采样点 / 6400 字节）** 的高并发无锁环形队列；
* **Pre-roll 预加载**：会话刚建立时，缓冲前 20ms 的数据后再启动推流，杜绝冷启动爆音或破音；
* **防积压机制**：若缓冲区由于系统卡顿超过 300ms，自动平滑丢弃最老旧的无声/微弱帧，确保语音输入无明显尾部延迟。

### 3. C# NAudio WASAPI 推流核心逻辑
```csharp
using NAudio.Wave;
using NAudio.CoreAudioApi;

public class VirtualAudioStreamer : IDisposable
{
    private WasapiOut? _wasapiOut;
    private BufferedWaveProvider? _waveProvider;
    private readonly WaveFormat _format = new WaveFormat(16000, 16, 1);

    public void Initialize(MMDevice cableInputDevice)
    {
        // 创建共享模式的低延迟 WASAPI 输出
        _wasapiOut = new WasapiOut(cableInputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 20);
        
        _waveProvider = new BufferedWaveProvider(_format)
        {
            BufferDuration = TimeSpan.FromMilliseconds(500),
            DiscardOnBufferOverflow = true
        };

        _wasapiOut.Init(_waveProvider);
        _wasapiOut.Play();
    }

    public void WritePcmData(byte[] pcmChunk)
    {
        _waveProvider?.AddSamples(pcmChunk, 0, pcmChunk.Length);
    }

    public void Dispose()
    {
        _wasapiOut?.Stop();
        _wasapiOut?.Dispose();
        _wasapiOut = null;
    }
}
```

### 4. 实时音量反馈 (VU Meter)
推流管线在向虚拟声卡写入采样点时，同步计算当前帧的 RMS（均方根）音量值，归一化为 `0.0 ~ 1.0`，通知 UI 线程动态刷新麦克风波形指示器，让用户直观看到遥控器正在拾音。
