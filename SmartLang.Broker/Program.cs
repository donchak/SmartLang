using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace SmartLang.Broker;

internal static class Program {
    [STAThread]
    static int Main(string[] args) {
        ApplicationConfiguration.Initialize();
        if(TryHandleSetupCommand(args, out var exitCode)) {
            return exitCode;
        }

        if(args.Length != 0) {
            AppLog.Write($"Broker rejected an unsupported command line with {args.Length} argument(s).");
            return 87;
        }

        using var mutex = new Mutex(true, BuildMutexName(), out var createdNew);
        if(!createdNew) {
            return 0;
        }

        AppLog.Write($"Starting SmartLang broker {Application.ProductVersion}.");
        using var context = new BrokerApplicationContext();
        Application.Run(context);
        AppLog.Write("SmartLang broker exited normally.");
        return 0;
    }

    static bool TryHandleSetupCommand(string[] args, out int exitCode) {
        exitCode = 0;
        if(args.Length != 2 ||
            args[0] is not ("--install-tasks" or "--remove-tasks")) {
            return false;
        }

        try {
            var taskManager = new ScheduledTaskManager();
            if(args[0] == "--install-tasks") {
                var trayPath = Path.Combine(AppContext.BaseDirectory, "SmartLang.exe");
                try {
                    taskManager.Register(
                        trayPath,
                        Environment.ProcessPath
                            ?? Path.Combine(AppContext.BaseDirectory, "SmartLang.Broker.exe"),
                        startWithWindows: true,
                        administratorSupport: true,
                        args[1]);
                } catch(Exception exception) {
                    AppLog.Write(
                        "Administrator task registration is unavailable; " +
                        $"registering tray fallback only: {exception.Message}");
                    taskManager.RegisterTray(trayPath, startWithWindows: true, args[1]);
                }
            } else {
                taskManager.Stop(args[1]);
                taskManager.Remove(args[1]);
            }
        } catch(Exception exception) {
            AppLog.Write($"Setup command {args[0]} failed with " + $"{exception.GetType().Name}: {exception.Message}");
            exitCode = 1;
        }

        return true;
    }

    static string BuildMutexName() {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..16];
        var sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        return $@"Local\SmartLang.Broker.{sessionId}.{hash}";
    }
}
