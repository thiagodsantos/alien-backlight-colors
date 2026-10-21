using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using AlienCommandCenter.Services;
using AlienCommandCenter.ViewModels;

namespace AlienCommandCenter;

public partial class MainWindow : Window
{
    private const int WmInput = 0x00FF;
    private const uint RidevInputSink = 0x00000100;

    private readonly AlienAwElcKeyboardLightingService _lightingService = new();
    private readonly Forms.NotifyIcon _trayIcon;
    private HwndSource? _windowSource;
    private bool _allowClose;
    private bool _hasShownTrayNotice;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Application.Current.SessionEnding += OnApplicationSessionEnding;
        DataContext = new MainViewModel(new PresetStore(), new ActiveLightingStore(), _lightingService);

        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("Open Alien Backlight Colors", null, (_, _) => RestoreWindow());
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitFromTray());
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Alien Backlight Colors is running",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreWindow();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowProc);

        var keyboard = new RawInputDevice
        {
            UsagePage = 0x01,
            Usage = 0x06,
            Flags = RidevInputSink,
            Target = handle
        };
        _ = RegisterRawInputDevices(ref keyboard, 1, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmInput)
        {
            return IntPtr.Zero;
        }

        _ = _lightingService.RefreshBacklightAsync();
        handled = true;
        return DefWindowProc(hwnd, (uint)message, wParam, lParam);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            if (!_hasShownTrayNotice)
            {
                _trayIcon.ShowBalloonTip(
                    3000,
                    "Keyboard lighting stays active",
                    "The app is still running in the notification area so it can restore your lighting after keyboard activity.",
                    Forms.ToolTipIcon.Info);
                _hasShownTrayNotice = true;
            }

            return;
        }

        base.OnClosing(e);
    }

    private void OnApplicationSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        _allowClose = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        Application.Current.SessionEnding -= OnApplicationSessionEnding;
        _windowSource?.RemoveHook(WindowProc);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _lightingService.Dispose();
        base.OnClosed(e);
    }

    private void RestoreWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void ExitFromTray()
    {
        _allowClose = true;
        Close();
    }

    private void RemoveAwcc_Click(object sender, RoutedEventArgs e)
    {
        var uninstallCommand = AwccUninstaller.FindRegisteredUninstaller();
        if (uninstallCommand is null)
        {
            MessageBox.Show(
                this,
                "No validated Alien Command Center Package Manager uninstaller was found. No files were changed. Remove AWCC through Windows Settings > Apps instead.",
                "AWCC uninstaller unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "This launches Dell's registered AWCC uninstaller with administrator permission. It can remove Alien lighting, thermal, and profile features. This app will not force-delete drivers, services, registry keys, or folders. Continue?",
            "Remove Alien Command Center",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            using var uninstaller = AwccUninstaller.StartElevated(uninstallCommand);
            if (uninstaller is null)
            {
                MessageBox.Show(this, "Windows did not start the AWCC uninstaller.", "AWCC removal", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _allowClose = true;
            Application.Current.Shutdown();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            MessageBox.Show(this, "Administrator approval was cancelled; no removal was started.", "AWCC removal", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not start the AWCC uninstaller: {exception.Message}", "AWCC removal", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(ref RawInputDevice devices, uint deviceCount, uint structureSize);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private void PickPrimaryColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ZoneViewModel zone })
        {
            return;
        }

        var picker = new ColorPickerWindow(zone.ColorHex) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            zone.ColorHex = picker.SelectedColorHex;
        }
    }
}
