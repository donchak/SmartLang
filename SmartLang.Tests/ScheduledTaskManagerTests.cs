namespace SmartLang.Tests;

public sealed class ScheduledTaskManagerTests
{
    [Fact]
    public void TaskNamesAreStableAndSeparatedByRole()
    {
        const string sid = "S-1-5-21-100-200-300-1001";

        var tray = ScheduledTaskManager.TrayTaskName(sid);
        var broker = ScheduledTaskManager.BrokerTaskName(sid);

        Assert.StartsWith("Tray-", tray);
        Assert.StartsWith("Broker-", broker);
        Assert.NotEqual(tray, broker);
        Assert.Equal(tray, ScheduledTaskManager.TrayTaskName(sid));
        Assert.Equal(broker, ScheduledTaskManager.BrokerTaskName(sid));
    }

    [Fact]
    public void DifferentUsersReceiveDifferentTaskNames()
    {
        Assert.NotEqual(
            ScheduledTaskManager.BrokerTaskName("S-1-5-21-1"),
            ScheduledTaskManager.BrokerTaskName("S-1-5-21-2"));
    }
}
