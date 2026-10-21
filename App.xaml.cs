using System.Diagnostics;
using System.Threading;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace AlienCommandCenter;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        using var currentProcess = Process.GetCurrentProcess();
        var existingProcessId = 0;
        foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
        {
            using (process)
            {
                if (process.Id != currentProcess.Id)
                {
                    existingProcessId = process.Id;
                    break;
                }
            }
        }

        if (existingProcessId != 0)
        {
            MessageBox.Show(
                $"Alien Backlight Colors is already running in the notification area (PID {existingProcessId}). Restore it from there instead of starting a second controller instance.",
                "Keyboard controller already in use",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _instanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\AlienCommandCenter.KeyboardStudio",
            createdNew: out _ownsInstanceMutex);
        if (!_ownsInstanceMutex)
        {
            MessageBox.Show(
                "Alien Backlight Colors is already running in the notification area. Restore it from there instead of starting a second controller instance.",
                "Keyboard controller already in use",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
