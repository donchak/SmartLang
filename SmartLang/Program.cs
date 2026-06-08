namespace SmartLang;

static class Program
{
    [STAThread]
    static void Main()
    {
        AppLog.Write($"Starting SmartLang {Application.ProductVersion}.");
        ApplicationConfiguration.Initialize();

        using var singleInstance = new SingleInstanceCoordinator();
        if (!singleInstance.IsFirstInstance)
        {
            AppLog.Write("Another instance is already running; opening its Settings window.");
            singleInstance.SignalExistingInstance();
            return;
        }

        using var context = new SmartLangApplicationContext(singleInstance);
        Application.Run(context);
        AppLog.Write("SmartLang exited normally.");
    }
}
