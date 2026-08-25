using TuckPane.Services;
using Microsoft.UI.Xaml;
using System.Security.Cryptography;
using System.Text;

namespace TuckPane;

public partial class App : Application
{
    private readonly SingleInstanceGuard _singleInstance = CreateSingleInstanceGuard();
    private AppHost? _host;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            AppLogger.Error("Unhandled UI exception", args.Exception);
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!_singleInstance.IsPrimary)
        {
            if (!_singleInstance.SignalPrimary()) SingleInstanceGuard.ShowLegacyInstanceMessage();
            Exit();
            return;
        }

        try
        {
            _host = new AppHost();
            bool startup = Environment.GetCommandLineArgs().Any(argument => argument.Equals("--startup", StringComparison.OrdinalIgnoreCase));
            await _host.InitializeAsync(startup);
            _singleInstance.Listen(() => _host.OpenConsole());
        }
        catch (Exception ex)
        {
            AppLogger.Error("TuckPane 初始化失败。", ex);
            Exit();
        }
    }

    private static SingleInstanceGuard CreateSingleInstanceGuard()
    {
        const string name = "TuckPane-019d2f2d-0bfb-7ff0-98f5-d93093bb0b5d";
        const string legacyName = "GlassFolder-019d2f2d-0bfb-7ff0-98f5-d93093bb0b5d";
        string? testRoot = Environment.GetEnvironmentVariable("TUCKPANE_TEST_ROOT");
        if (string.IsNullOrWhiteSpace(testRoot)) return new SingleInstanceGuard(name, legacyName);
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(testRoot))))[..12];
        return new SingleInstanceGuard($"{name}-{suffix}", $"{legacyName}-{suffix}");
    }
}
