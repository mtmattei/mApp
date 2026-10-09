using Microsoft.UI.Windowing;
using Uno.Resizetizer;

namespace AppOrbit;

public partial class App : Application
{
    public App()
    {
        // APP_ORBIT_THEME=dark|light pins the theme (a harness captures both); otherwise the system theme
        var theme = Environment.GetEnvironmentVariable("APP_ORBIT_THEME");
        if (theme == "dark") RequestedTheme = ApplicationTheme.Dark;
        else if (theme == "light") RequestedTheme = ApplicationTheme.Light;
        InitializeComponent();
        // the design viewport, asked for before the window exists (a resize after creation races the X11 surface); APP_ORBIT_SIZE=WxH overrides
        var size = Environment.GetEnvironmentVariable("APP_ORBIT_SIZE")?.Split('x');
        var w = size?.Length == 2 && int.TryParse(size[0], out var pw) ? pw : 1440;
        var h = size?.Length == 2 && int.TryParse(size[1], out var ph) ? ph : 900;
        DesiredSize = new Windows.Graphics.SizeInt32 { Width = w, Height = h };
        try
        {
            Windows.UI.ViewManagement.ApplicationView.PreferredLaunchViewSize = new Windows.Foundation.Size(w, h);
            Windows.UI.ViewManagement.ApplicationView.PreferredLaunchWindowingMode = Windows.UI.ViewManagement.ApplicationViewWindowingMode.PreferredLaunchViewSize;
        }
        catch { }
    }

    protected Window? MainWindow { get; private set; }

    /// <summary>The window size asked for at launch; the shell asks again until the XAML root reports it (X11 applies a resize intermittently).</summary>
    public static Windows.Graphics.SizeInt32 DesiredSize { get; private set; }

    public static Window? Current { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window();
        Current = MainWindow;
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
