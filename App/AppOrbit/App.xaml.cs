using Microsoft.UI.Windowing;
using Uno.Resizetizer;

namespace AppOrbit;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window();
#if DEBUG
        // UseStudio() blocks the window when no DevServer is reachable, so headless runs opt out.
        if (Environment.GetEnvironmentVariable("APP_NO_HOTDESIGN") != "1")
        {
            MainWindow.UseStudio();
        }
#endif

        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            MainWindow.Content = rootFrame;
            rootFrame.NavigationFailed += OnNavigationFailed;
        }

        if (rootFrame.Content == null)
        {
            rootFrame.Navigate(typeof(ShellPage), args.Arguments);
        }

        MainWindow.SetWindowIcon();

        // the design viewport; a harness can pin another size with APP_ORBIT_SIZE=WxH
        var size = Environment.GetEnvironmentVariable("APP_ORBIT_SIZE")?.Split('x');
        var w = size?.Length == 2 && int.TryParse(size[0], out var pw) ? pw : 1440;
        var h = size?.Length == 2 && int.TryParse(size[1], out var ph) ? ph : 900;
        try { MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = w, Height = h }); } catch { }

        // The Win32 GL render thread access-violates at degenerate sizes; the shell needs the width anyway.
        if (MainWindow.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1100;
            presenter.PreferredMinimumHeight = 640;
        }

        MainWindow.Activate();
    }

    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Failed to load {e.SourcePageType.FullName}: {e.Exception}");
    }

    public static void InitializeLogging()
    {
#if DEBUG
        var factory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("Uno", LogLevel.Warning);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);
        });

        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;
#if HAS_UNO
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
#endif
    }
}
