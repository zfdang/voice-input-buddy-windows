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
                int rawDataSize = (int)dwSize - headerSize;
                if (rawDataSize > 0)
                {
                    byte[] rawBytes = new byte[rawDataSize];
                    Marshal.Copy(IntPtr.Add(buffer, headerSize), rawBytes, 0, rawDataSize);

                    // Check for G9N9N Report 2 or standard Consumer Control usages
                    if (G9N9NProtocol.TryParseReport2(rawBytes, out var buttons))
                    {
                        foreach (var btn in buttons)
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
        _isRegistered = false;
    }
}
