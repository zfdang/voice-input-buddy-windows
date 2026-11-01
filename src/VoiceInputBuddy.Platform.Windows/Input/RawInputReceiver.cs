using System.Runtime.InteropServices;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Core.Protocols;

namespace VoiceInputBuddy.Platform.Windows.Input;

public sealed class RawInputReceiver : IDisposable
{
    private const int WM_INPUT = 0x00FF;
    private const int RID_INPUT = 0x10000003;
    private const int RIDEV_INPUTSINK = 0x00000100;

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

    public event Action<RemoteButton, bool>? ButtonEvent;

    private IntPtr _hwnd;
    private bool _isRegistered;
    private readonly HashSet<RemoteButton> _pressedButtons = new();
    private readonly object _lock = new();

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
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref dwSize, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == dwSize)
            {
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

                // Parse current held buttons
                var currentHeld = new HashSet<RemoteButton>();
                int reportSize = (int)dwSizeHid;
                for (int i = 0; i < (int)dwCount; i++)
                {
                    var slice = rawBytes.AsSpan(i * reportSize, reportSize);
                    if (G9N9NProtocol.TryParseReport2(slice, out var buttons))
                    {
                        foreach (var b in buttons) currentHeld.Add(b);
                    }
                }

                lock (_lock)
                {
                    // Newly pressed buttons -> KeyDown (isDown = true)
                    foreach (var btn in currentHeld)
                    {
                        if (!_pressedButtons.Contains(btn))
                        {
                            ButtonEvent?.Invoke(btn, true);
                        }
                    }

                    // Released buttons -> KeyUp (isDown = false)
                    foreach (var btn in _pressedButtons)
                    {
                        if (!currentHeld.Contains(btn))
                        {
                            ButtonEvent?.Invoke(btn, false);
                        }
                    }

                    _pressedButtons.Clear();
                    foreach (var btn in currentHeld)
                    {
                        _pressedButtons.Add(btn);
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
        _isRegistered = false;
        lock (_lock)
        {
            _pressedButtons.Clear();
        }
    }
}
