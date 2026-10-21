using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AlienCommandCenter.Services;
using AlienCommandCenter.ViewModels;

namespace AlienCommandCenter;

public partial class MainWindow : Window
{
    private const int WmInput = 0x00FF;
    private const uint RidevInputSink = 0x00000100;

    private readonly AlienwareAwElcKeyboardLightingService _lightingService = new();
    private HwndSource? _windowSource;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        DataContext = new MainViewModel(new PresetStore(), _lightingService);
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

    protected override void OnClosed(EventArgs e)
    {
        _windowSource?.RemoveHook(WindowProc);
        _lightingService.Dispose();
        base.OnClosed(e);
    }

    private void RemoveAwcc_Click(object sender, RoutedEventArgs e)
    {
        var uninstallCommand = AwccUninstaller.FindRegisteredUninstaller();
        if (uninstallCommand is null)
        {
            MessageBox.Show(
                this,
                "No validated Alienware Command Center Package Manager uninstaller was found. No files were changed. Remove AWCC through Windows Settings > Apps instead.",
                "AWCC uninstaller unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "This launches Dell's registered AWCC uninstaller with administrator permission. It can remove Alienware lighting, thermal, and profile features. This app will not force-delete drivers, services, registry keys, or folders. Continue?",
            "Remove Alienware Command Center",
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

            _lightingService.Dispose();
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

    private void PickPrimaryColor_Click(object sender, RoutedEventArgs e) => PickColor(sender, isPrimary: true);

    private void PickSecondaryColor_Click(object sender, RoutedEventArgs e) => PickColor(sender, isPrimary: false);

    private void PickColor(object sender, bool isPrimary)
    {
        if (sender is not FrameworkElement { DataContext: ZoneViewModel zone })
        {
            return;
        }

        var initialColor = isPrimary ? zone.ColorHex : zone.SecondaryColorHex;
        var picker = new ColorPickerWindow(initialColor) { Owner = this };
        if (picker.ShowDialog() != true)
        {
            return;
        }

        if (isPrimary)
        {
            zone.ColorHex = picker.SelectedColorHex;
        }
        else
        {
            zone.SecondaryColorHex = picker.SelectedColorHex;
        }
    }
}
