namespace SvitloTray.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        // Second launch: show the first instance's window and exit.
        if (!SingleInstance.TryClaim())
        {
            SingleInstance.SignalFirstInstance();
            Environment.Exit(0);
        }

        this.InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
