namespace SmartLang;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var singleInstance = new SingleInstanceCoordinator();
        if (!singleInstance.IsFirstInstance)
        {
            singleInstance.SignalExistingInstance();
            return;
        }

        using var context = new SmartLangApplicationContext(singleInstance);
        Application.Run(context);
    }
}
