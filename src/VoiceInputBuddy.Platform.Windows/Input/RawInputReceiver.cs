using System.Runtime.InteropServices;
using VoiceInputBuddy.Core.Diagnostics;
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
    private const uint RIDI_DEVICEINFO = 0x2000000b;

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const uint RIM_TYPEHID = 2;

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

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_PropertyW", SetLastError = true)]
    private static extern uint CM_Get_Device_Interface_Property(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceInterface,
        ref DEVPROPKEY PropertyKey,
        out uint PropertyType,
        [Out] byte[] PropertyBuffer,
        ref uint PropertyBufferSize,
        uint ulFlags);

    public event Action<IntPtr, RemoteButton, bool>? ButtonEvent;

    public Func<IntPtr, string, bool>? DeviceFilter { get; set; }

    private IntPtr _hwnd;
    private bool _isRegistered;
    private readonly Dictionary<IntPtr, string> _devicePathCache = new();
    private readonly Dictionary<IntPtr, HashSet<RemoteButton>> _devicePressedButtons = new();
    private readonly object _lock = new();

    public static Guid? GetDeviceContainerId(string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath)) return null;

        try
        {
            var key = new DEVPROPKEY
            {
                fmtid = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"),
                pid = 2 // DEVPKEY_Device_ContainerId
            };

            uint bufferSize = 16;
            byte[] buffer = new byte[16];
            uint propType = 0;
            uint cr = CM_Get_Device_Interface_Property(devicePath, ref key, out propType, buffer, ref bufferSize, 0);
            if (cr == 0 && bufferSize == 16)
            {
                return new Guid(buffer);
            }
        }
        catch
        {
            // Graceful fallback for non-Windows test environments
        }

        return null;
    }

    public static bool GetHidDeviceInfo(IntPtr hDevice, out uint vendorId, out uint productId)
    {
        vendorId = 0;
        productId = 0;
        if (hDevice == IntPtr.Zero) return false;

        try
        {
            uint size = 0;
            GetRawInputDeviceInfo(hDevice, RIDI_DEVICEINFO, IntPtr.Zero, ref size);
            if (size == 0) return false;

            IntPtr pData = Marshal.AllocHGlobal((int)size);
            try
            {
                Marshal.WriteInt32(pData, (int)size); // cbSize
                if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICEINFO, pData, ref size) == size)
                {
                    uint dwType = (uint)Marshal.ReadInt32(pData, 4);
                    if (dwType == 2) // RIM_TYPEHID
                    {
                        // In RID_DEVICE_INFO, the union starts at offset 8
                        vendorId = (uint)Marshal.ReadInt32(pData, 8);
                        productId = (uint)Marshal.ReadInt32(pData, 12);
                        return true;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pData);
            }
        }
        catch
        {
            // Graceful fallback
        }

        return false;
    }

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
            },
            new()
            {
                usUsagePage = 0x01, // Generic Desktop Controls
                usUsage = 0x06,     // Keyboard
                dwFlags = RIDEV_INPUTSINK,
                hwndTarget = hwnd
            }
        };

        _isRegistered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        if (_isRegistered)
        {
            AppLogger.Info($"RawInput devices successfully registered for HWND 0x{hwnd:X}", "RawInputReceiver");
        }
        else
        {
            int err = Marshal.GetLastWin32Error();
            AppLogger.Error($"Failed to register RawInput devices for HWND 0x{hwnd:X}. Win32Error={err}", component: "RawInputReceiver");
        }
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

            if (header.dwType == RIM_TYPEKEYBOARD)
            {
                if ((int)dwSize < headerSize + Marshal.SizeOf<RAWKEYBOARD>()) return;

                ushort flags = (ushort)Marshal.ReadInt16(buffer, headerSize + 2);
                ushort vkey = (ushort)Marshal.ReadInt16(buffer, headerSize + 6);
                bool isDown = (flags & 1) == 0;

                var btn = MapVirtualKeyToRemoteButton(vkey);
                if (btn == RemoteButton.None) return;

                lock (_lock)
                {
                    if (!_devicePressedButtons.TryGetValue(hDevice, out var pressedButtons))
                    {
                        pressedButtons = new HashSet<RemoteButton>();
                        _devicePressedButtons[hDevice] = pressedButtons;
                    }

                    if (isDown)
                    {
                        if (pressedButtons.Add(btn))
                        {
                            AppLogger.Info($"RawInput Keyboard event: {btn} Down (vkey=0x{vkey:X2}, hDevice=0x{hDevice:X})", "RawInputReceiver");
                            ButtonEvent?.Invoke(hDevice, btn, true);
                        }
                    }
                    else
                    {
                        if (pressedButtons.Remove(btn))
                        {
                            AppLogger.Info($"RawInput Keyboard event: {btn} Up (vkey=0x{vkey:X2}, hDevice=0x{hDevice:X})", "RawInputReceiver");
                            ButtonEvent?.Invoke(hDevice, btn, false);
                        }
                    }
                }
                return;
            }

            if (header.dwType != RIM_TYPEHID) return;

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
                    bool recognized = false;
                    List<RemoteButton> currentButtons;
                    if (G9N9NProtocol.TryParseReport(slice, out currentButtons))
                    {
                        recognized = true;
                    }
                    else if (XiaomiProtocol.TryParseReport(slice, out currentButtons))
                    {
                        recognized = true;
                    }

                    if (!recognized)
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
                        AppLogger.Info($"RawInput HID event: {btn} Up (hDevice=0x{hDevice:X})", "RawInputReceiver");
                        ButtonEvent?.Invoke(hDevice, btn, false);
                    }

                    // 2. Newly pressed buttons -> KeyDown (true)
                    foreach (var btn in currentReportButtons)
                    {
                        if (pressedButtons.Add(btn))
                        {
                            AppLogger.Info($"RawInput HID event: {btn} Down (hDevice=0x{hDevice:X})", "RawInputReceiver");
                            ButtonEvent?.Invoke(hDevice, btn, true);
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

    public static RemoteButton MapVirtualKeyToRemoteButton(ushort vkey)
    {
        return vkey switch
        {
            0x26 => RemoteButton.Up,           // VK_UP
            0x28 => RemoteButton.Down,         // VK_DOWN
            0x25 => RemoteButton.Left,         // VK_LEFT
            0x27 => RemoteButton.Right,        // VK_RIGHT
            0x0D => RemoteButton.Ok,           // VK_RETURN
            0x1B => RemoteButton.Back,         // VK_ESCAPE
            0x24 => RemoteButton.Home,         // VK_HOME
            0xAC => RemoteButton.Home,         // VK_BROWSER_HOME
            0x12 => RemoteButton.Menu,         // VK_MENU
            0x5D => RemoteButton.Menu,         // VK_APPS
            0xAF => RemoteButton.VolumeUp,     // VK_VOLUME_UP
            0xAE => RemoteButton.VolumeDown,   // VK_VOLUME_DOWN
            0xAD => RemoteButton.Mute,         // VK_VOLUME_MUTE
            0xB3 => RemoteButton.PlayPause,    // VK_MEDIA_PLAY_PAUSE
            _ => RemoteButton.None
        };
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
                },
                new()
                {
                    usUsagePage = 0x01,
                    usUsage = 0x06,
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
