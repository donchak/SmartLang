using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace SmartLang;

public sealed class ScheduledTaskManager
{
    private const int TaskActionExec = 0;
    private const int TaskCreateOrUpdate = 6;
    private const int TaskInstancesIgnoreNew = 2;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskRunLevelHighest = 1;
    private const int TaskRunLevelLua = 0;
    private const int TaskTriggerLogon = 9;

    public void Register(
        string trayExecutablePath,
        string brokerExecutablePath,
        bool startWithWindows,
        bool administratorSupport,
        string? userSid = null)
    {
        userSid ??= WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Could not determine the current user SID.");

        dynamic service = CreateService();
        try
        {
            dynamic folder = GetOrCreateFolder(service, userSid);

            RegisterTask(
                service,
                folder,
                TrayTaskName(userSid),
                trayExecutablePath,
                userSid,
                highest: false,
                enabled: startWithWindows);
            RegisterTask(
                service,
                folder,
                BrokerTaskName(userSid),
                brokerExecutablePath,
                userSid,
                highest: true,
                enabled: startWithWindows && administratorSupport);
        }
        finally
        {
            Release(service);
        }
    }

    public void RegisterTray(
        string trayExecutablePath,
        bool startWithWindows,
        string? userSid = null)
    {
        userSid ??= WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Could not determine the current user SID.");
        dynamic service = CreateService();
        try
        {
            dynamic folder = GetOrCreateFolder(service, userSid);
            RegisterTask(
                service,
                folder,
                TrayTaskName(userSid),
                trayExecutablePath,
                userSid,
                highest: false,
                enabled: startWithWindows);
        }
        finally
        {
            Release(service);
        }
    }

    public void Configure(
        bool startWithWindows,
        bool administratorSupport,
        string? userSid = null)
    {
        userSid ??= WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Could not determine the current user SID.");
        dynamic service = CreateService();
        try
        {
            dynamic folder = service.GetFolder("\\SmartLang");
            folder.GetTask(TrayTaskName(userSid)).Enabled = startWithWindows;
            folder.GetTask(BrokerTaskName(userSid)).Enabled =
                startWithWindows && administratorSupport;
        }
        finally
        {
            Release(service);
        }
    }

    public bool RunBroker(string? userSid = null)
    {
        return RunTask(BrokerTaskName, userSid);
    }

    public bool RunTray(string? userSid = null)
    {
        return RunTask(TrayTaskName, userSid);
    }

    public void Stop(string? userSid = null)
    {
        userSid ??= WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Could not determine the current user SID.");
        dynamic service = CreateService();
        try
        {
            dynamic folder = service.GetFolder("\\SmartLang");
            TryStop(folder, TrayTaskName(userSid));
            TryStop(folder, BrokerTaskName(userSid));
        }
        catch (Exception exception) when (
            exception is COMException or FileNotFoundException)
        {
        }
        finally
        {
            Release(service);
        }
    }

    private static bool RunTask(
        Func<string, string> taskNameFactory,
        string? userSid)
    {
        userSid ??= WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Could not determine the current user SID.");
        dynamic service = CreateService();
        try
        {
            dynamic folder = service.GetFolder("\\SmartLang");
            dynamic task = folder.GetTask(taskNameFactory(userSid));
            task.Run(null);
            return true;
        }
        catch (Exception exception) when (
            exception is COMException or FileNotFoundException or
            InvalidOperationException or UnauthorizedAccessException)
        {
            AppLog.Write($"Could not run the broker task: {exception.Message}");
            return false;
        }
        finally
        {
            Release(service);
        }
    }

    public void Remove(string? userSid = null)
    {
        userSid ??= WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Could not determine the current user SID.");
        dynamic service = CreateService();
        try
        {
            dynamic folder = service.GetFolder("\\SmartLang");
            TryDelete(folder, TrayTaskName(userSid));
            TryDelete(folder, BrokerTaskName(userSid));
        }
        catch (Exception exception) when (
            exception is COMException or FileNotFoundException)
        {
        }
        finally
        {
            Release(service);
        }
    }

    internal static string TrayTaskName(string userSid) => $"Tray-{HashSid(userSid)}";

    internal static string BrokerTaskName(string userSid) => $"Broker-{HashSid(userSid)}";

    private static dynamic CreateService()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new PlatformNotSupportedException("Task Scheduler 2.0 is unavailable.");
        dynamic service = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Could not create the Task Scheduler service.");
        service.Connect();
        return service;
    }

    private static void RegisterTask(
        dynamic service,
        dynamic folder,
        string taskName,
        string executablePath,
        string userSid,
        bool highest,
        bool enabled)
    {
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "Starts SmartLang for this Windows user.";
        definition.Principal.UserId = userSid;
        definition.Principal.LogonType = TaskLogonInteractiveToken;
        definition.Principal.RunLevel = highest ? TaskRunLevelHighest : TaskRunLevelLua;

        dynamic trigger = definition.Triggers.Create(TaskTriggerLogon);
        trigger.UserId = userSid;
        trigger.Enabled = true;

        dynamic action = definition.Actions.Create(TaskActionExec);
        action.Path = Path.GetFullPath(executablePath);
        action.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath));

        definition.Settings.Enabled = enabled;
        definition.Settings.AllowDemandStart = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = "PT0S";
        definition.Settings.MultipleInstances = TaskInstancesIgnoreNew;
        if (highest)
        {
            definition.Settings.RestartCount = 3;
            definition.Settings.RestartInterval = "PT1M";
        }

        folder.RegisterTaskDefinition(
            taskName,
            definition,
            TaskCreateOrUpdate,
            userSid,
            null,
            TaskLogonInteractiveToken,
            BuildSecurityDescriptor(userSid));
    }

    private static dynamic GetOrCreateFolder(dynamic service, string userSid)
    {
        dynamic root = service.GetFolder("\\");
        try
        {
            return root.GetFolder("SmartLang");
        }
        catch (Exception exception) when (
            exception is COMException or FileNotFoundException)
        {
            return root.CreateFolder(
                "SmartLang",
                BuildSecurityDescriptor(userSid));
        }
    }

    private static void TryDelete(dynamic folder, string taskName)
    {
        try
        {
            folder.DeleteTask(taskName, 0);
        }
        catch (Exception exception) when (
            exception is COMException or FileNotFoundException)
        {
        }
    }

    private static void TryStop(dynamic folder, string taskName)
    {
        try
        {
            folder.GetTask(taskName).Stop(0);
        }
        catch (Exception exception) when (
            exception is COMException or FileNotFoundException)
        {
        }
    }

    private static string HashSid(string userSid) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userSid)))[..16];

    private static string BuildSecurityDescriptor(string userSid) =>
        $"D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGWGX;;;{userSid})";

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
