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

        Application.Run(new SmartLangApplicationContext(singleInstance));
    }
}
