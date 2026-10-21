using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AlienCommandCenter.Services;

public sealed record AwccUninstallCommand(string DisplayName, string ExecutablePath, string Arguments);

public static class AwccUninstaller
{
    private const string PackageManagerDisplayName = "Alienware Command Center Package Manager";

    public static AwccUninstallCommand? FindRegisteredUninstaller()
    {
        var uninstallRoots = new[] { Registry.LocalMachine, Registry.CurrentUser };
        var uninstallPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };
        var installShieldRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "InstallShield Installation Information")) + Path.DirectorySeparatorChar;

        foreach (var root in uninstallRoots)
        {
            foreach (var uninstallPath in uninstallPaths)
            {
                using var uninstallKey = root.OpenSubKey(uninstallPath);
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var keyName in uninstallKey.GetSubKeyNames())
                {
                    using var productKey = uninstallKey.OpenSubKey(keyName);
                    var displayName = productKey?.GetValue("DisplayName") as string;
                    if (!string.Equals(displayName, PackageManagerDisplayName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var uninstallString = productKey?.GetValue("UninstallString") as string;
                    if (!TryParseUninstallCommand(uninstallString, installShieldRoot, out var executablePath, out var arguments))
                    {
                        continue;
                    }

                    return new AwccUninstallCommand(displayName ?? PackageManagerDisplayName, executablePath, arguments);
                }
            }
        }

        return null;
    }

    public static Process? StartElevated(AwccUninstallCommand command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            Arguments = command.Arguments,
            WorkingDirectory = Path.GetDirectoryName(command.ExecutablePath)!,
            UseShellExecute = true,
            Verb = "runas"
        };

        return Process.Start(startInfo);
    }

    private static bool TryParseUninstallCommand(
        string? uninstallString,
        string installShieldRoot,
        out string executablePath,
        out string arguments)
    {
        executablePath = string.Empty;
        arguments = string.Empty;
        if (string.IsNullOrWhiteSpace(uninstallString))
        {
            return false;
        }

        var command = uninstallString.Trim();
        if (!command.StartsWith('"'))
        {
            return false;
        }

        var closingQuote = command.IndexOf('"', 1);
        if (closingQuote <= 1)
        {
            return false;
        }

        executablePath = Path.GetFullPath(command[1..closingQuote]);
        arguments = command[(closingQuote + 1)..].Trim();

        return string.Equals(Path.GetFileName(executablePath), "AWCCInstallationManager.exe", StringComparison.OrdinalIgnoreCase) &&
            executablePath.StartsWith(installShieldRoot, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(executablePath) &&
            arguments.Contains("-remove", StringComparison.OrdinalIgnoreCase);
    }
}
