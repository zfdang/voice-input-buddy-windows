using System.Runtime.InteropServices;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Core.Protocols;

namespace VoiceInputBuddy.Platform.Windows.Input;

public sealed class RawInputReceiver : IDisposable
{
    private const int WM_INPUT = 0x00FF;
    private const int RID_INPUT = 0x10000003;
    private const int RIDEV_INPUTSINK = 0x00000100;
    private const int RIDEV_REMOVE = 0x00000001;
    private const uint RIDI_DEVICENAME = 0x20000007;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWHID
    {
        public uint dwSizeHid;
        public uint dwCount;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);

    public event Action<RemoteButton, bool>? ButtonEvent;

    public Func<IntPtr, string, bool>? DeviceFilter { get; set; }

    private IntPtr _hwnd;
    private bool _isRegistered;
    private readonly Dictionary<IntPtr, string> _devicePathCache = new();
    private readonly Dictionary<IntPtr, HashSet<RemoteButton>> _devicePressedButtons = new();
    private readonly object _lock = new();

    public static string? GetDevicePath(IntPtr hDevice)
    {
        if (hDevice == IntPtr.Zero) return null;
        uint charCount = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref charCount);
        if (charCount == 0) return null;

        IntPtr pData = Marshal.AllocHGlobal((int)(charCount * sizeof(char)));
        try
        {
            uint res = GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, pData, ref charCount);
            if (res > 0 && res != uint.MaxValue)
            {
                return Marshal.PtrToStringUni(pData);
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(pData);
        }
    }

    public bool Register(IntPtr hwnd)
    {
        _hwnd = hwnd;
        var devices = new RAWINPUTDEVICE[]
        {
            new()
            {
                usUsagePage = 0x0C, // Consumer Control
                usUsage = 0x01,     // Consumer Control Device
                dwFlags = RIDEV_INPUTSINK,
                hwndTarget = hwnd
            }
        };

        _isRegistered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        return _isRegistered;
    }

    public void ProcessMessage(IntPtr lParam)
    {
        uint dwSize = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
        if (dwSize == 0) return;

        IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref dwSize, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) != dwSize)
            {
                return;
            }

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            IntPtr hDevice = header.hDevice;

            string devicePath;
            lock (_lock)
            {
                if (!_devicePathCache.TryGetValue(hDevice, out var path))
                {
                    path = GetDevicePath(hDevice) ?? string.Empty;
                    _devicePathCache[hDevice] = path;
                }
                devicePath = path;
            }

            if (DeviceFilter != null && !DeviceFilter(hDevice, devicePath))
            {
                return;
            }

            int headerSize = Marshal.SizeOf<RAWINPUTHEADER>();
            int hidHeaderSize = Marshal.SizeOf<RAWHID>();
            int dataOffset = headerSize + hidHeaderSize;

            if ((int)dwSize < dataOffset) return;

            uint dwSizeHid = (uint)Marshal.ReadInt32(buffer, headerSize);
            uint dwCount = (uint)Marshal.ReadInt32(buffer, headerSize + 4);

            int totalRawBytes = (int)(dwSizeHid * dwCount);
            if (totalRawBytes <= 0 || dataOffset + totalRawBytes > (int)dwSize) return;

            byte[] rawBytes = new byte[totalRawBytes];
            Marshal.Copy(IntPtr.Add(buffer, dataOffset), rawBytes, 0, totalRawBytes);

            int reportSize = (int)dwSizeHid;

            lock (_lock)
            {
                if (!_devicePressedButtons.TryGetValue(hDevice, out var pressedButtons))
                {
                    pressedButtons = new HashSet<RemoteButton>();
                    _devicePressedButtons[hDevice] = pressedButtons;
                }

                for (int i = 0; i < (int)dwCount; i++)
                {
                    var slice = rawBytes.AsSpan(i * reportSize, reportSize);
                    if (!G9N9NProtocol.TryParseReport(slice, out var currentButtons))
                    {
                        continue;
                    }

                    var currentReportButtons = new HashSet<RemoteButton>(currentButtons);

                    // 1. Released buttons -> KeyUp (false)
                    var released = new List<RemoteButton>();
                    foreach (var btn in pressedButtons)
                    {
                        if (!currentReportButtons.Contains(btn))
                        {
                            released.Add(btn);
                        }
                    }

                    foreach (var btn in released)
                    {
                        pressedButtons.Remove(btn);
                        ButtonEvent?.Invoke(btn, false);
                    }

                    // 2. Newly pressed buttons -> KeyDown (true)
                    foreach (var btn in currentReportButtons)
                    {
                        if (pressedButtons.Add(btn))
                        {
                            ButtonEvent?.Invoke(btn, true);
                        }
                    }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_isRegistered && _hwnd != IntPtr.Zero)
        {
            var devices = new RAWINPUTDEVICE[]
            {
                new()
                {
                    usUsagePage = 0x0C,
                    usUsage = 0x01,
                    dwFlags = RIDEV_REMOVE,
                    hwndTarget = IntPtr.Zero
                }
            };
            RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            _isRegistered = false;
        }

        lock (_lock)
        {
            _devicePressedButtons.Clear();
            _devicePathCache.Clear();
        }
    }
}
