using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Windows.Forms;
using IOPath = System.IO.Path;

namespace BifitReport.Settings;

internal sealed class Options
{
    public string ConfigPath { get; private set; } =
        IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

    public bool NoElevate { get; private set; }

    public bool SelfTest { get; private set; }

    public string SelfTestOutput { get; private set; } =
        IOPath.Combine(IOPath.GetTempPath(), "bifit-settings-selftest.txt");

    public string InitialTab { get; private set; } = string.Empty;

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--config" when index + 1 < args.Length:
                    options.ConfigPath = IOPath.GetFullPath(args[++index]);
                    break;
                case "--no-elevate":
                    options.NoElevate = true;
                    break;
                case "--selftest":
                    options.SelfTest = true;
                    break;
                case "--output" when index + 1 < args.Length:
                    options.SelfTestOutput = IOPath.GetFullPath(args[++index]);
                    break;
                case "--tab" when index + 1 < args.Length:
                    options.InitialTab = args[++index];
                    break;
            }
        }

        return options;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var options = Options.Parse(args);
        if (options.SelfTest)
        {
            Environment.ExitCode = SelfTest.Run(options) ? 0 : 1;
            return;
        }

        if (!IsAdministrator() && !options.NoElevate)
        {
            RelaunchElevated(args);
            return;
        }

        try
        {
            var config = ConfigFile.Load(options.ConfigPath);
            Application.Run(new SettingsForm(config, options));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "BifitReport — настройки",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    public static bool IsAdministrator()
    {
        var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchElevated(IEnumerable<string> args)
    {
        var arguments = string.Join(" ", args.Select(QuoteArgument));
        var startInfo = new ProcessStartInfo(Application.ExecutablePath, arguments)
        {
            UseShellExecute = true,
            Verb = "RunAs"
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Пользователь отменил UAC.
        }
    }

    private static string QuoteArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"") + "\"";
}
