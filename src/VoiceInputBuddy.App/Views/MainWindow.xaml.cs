using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using VoiceInputBuddy.App.ViewModels;
using VoiceInputBuddy.App.Views.Pages;

namespace VoiceInputBuddy.App.Views;

public partial class MainWindow : Window
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 101;
    private const int WM_INPUT = 0x00FF;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private NOTIFYICONDATA _trayData;
    private bool _trayIconCreated = false;
    private uint _wmTaskbarCreated = 0;
    public bool IsExplicitExit { get; set; } = false;

    private DevicesPage? _devicesPage;
    private MappingPage? _mappingPage;
    private IntegrationsPage? _integrationsPage;
    private EnvironmentPage? _environmentPage;
    private SettingsPage? _settingsPage;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (DataContext is MainViewModel vm)
        {
            vm.RegisterRawInput(hwnd);
        }
        _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        InitializeTrayIcon(hwnd);
    }

    private void InitializeTrayIcon(IntPtr hwnd)
    {
        if (_trayIconCreated) return;
        try
        {
            IntPtr hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
            _trayData = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = hwnd,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = (uint)WM_TRAYICON,
                hIcon = hIcon,
                szTip = "Voice Input Buddy (Windows)"
            };
            _trayIconCreated = Shell_NotifyIcon(NIM_ADD, ref _trayData);
        }
        catch { }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_wmTaskbarCreated != 0 && (uint)msg == _wmTaskbarCreated)
        {
            _trayIconCreated = false;
            InitializeTrayIcon(hwnd);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_INPUT && DataContext is MainViewModel vm)
        {
            vm.ProcessRawInput(lParam);
        }
        else if (msg == WM_TRAYICON)
        {
            int eventId = lParam.ToInt32();
            if (eventId == WM_LBUTTONUP || eventId == WM_LBUTTONDBLCLK)
            {
                RestoreWindow();
                handled = true;
            }
            else if (eventId == WM_RBUTTONUP)
            {
                ShowTrayContextMenu();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void RestoreWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    private void ShowTrayContextMenu()
    {
        var menu = new ContextMenu
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };

        var openItem = new MenuItem { Header = "显示主界面" };
        openItem.Click += (_, _) => RestoreWindow();
        menu.Items.Add(openItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (_, _) =>
        {
            IsExplicitExit = true;
            Close();
            Application.Current.Shutdown();
        };
        menu.Items.Add(exitItem);

        menu.IsOpen = true;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (IsExplicitExit) return;

        if (DataContext is MainViewModel vm && vm.SettingsVm.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_trayIconCreated)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _trayData);
            _trayIconCreated = false;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            _devicesPage = new DevicesPage { DataContext = vm.DevicesVm };
            _mappingPage = new MappingPage { DataContext = vm.MappingVm };
            _integrationsPage = new IntegrationsPage { DataContext = vm.IntegrationsVm };
            _environmentPage = new EnvironmentPage { DataContext = vm.EnvironmentVm };
            _settingsPage = new SettingsPage { DataContext = vm.SettingsVm };

            vm.PropertyChanged += OnViewModelPropertyChanged;
            SwitchPage(vm.ActivePageName);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActivePageName) && sender is MainViewModel vm)
        {
            SwitchPage(vm.ActivePageName);
        }
    }

    private void SwitchPage(string pageName)
    {
        PageContentContainer.Content = pageName switch
        {
            "Devices" => _devicesPage,
            "Mapping" => _mappingPage,
            "Integrations" => _integrationsPage,
            "Environment" => _environmentPage,
            "Settings" => _settingsPage,
            _ => _devicesPage
        };
    }
}
