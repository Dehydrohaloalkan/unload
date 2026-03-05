using Spectre.Console;

namespace Unload.WebConsole;

internal static class RunDashboardBuilder
{
    public static Rows Build(UiSnapshot snapshot, string correlationId)
    {
        var statusColor = snapshot.Status switch
        {
            RunLifecycleStatus.Completed => "green",
            RunLifecycleStatus.Failed => "red",
            RunLifecycleStatus.Running => "deepskyblue1",
            _ => "yellow"
        };

        var info = new Grid();
        info.AddColumn();
        info.AddColumn();
        info.AddRow("[grey]CorrelationId[/]", Markup.Escape(correlationId));
        info.AddRow("[grey]Status[/]", $"[{statusColor}]{snapshot.Status}[/]");
        info.AddRow("[grey]Last step[/]", Markup.Escape(snapshot.LastStep?.ToString() ?? "-"));
        info.AddRow("[grey]Updated[/]", snapshot.UpdatedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "-");
        info.AddRow("[grey]Message[/]", Markup.Escape(snapshot.Message ?? "-"));

        var events = new Table().Border(TableBorder.Rounded).Title("Recent Events");
        events.AddColumn("Time");
        events.AddColumn("Step");
        events.AddColumn("Message");

        foreach (var item in snapshot.Events)
        {
            var stepColor = item.Step switch
            {
                RunnerStep.Failed => "red",
                RunnerStep.Completed => "green",
                RunnerStep.FileWritten => "deepskyblue1",
                _ => "grey"
            };

            events.AddRow(
                item.Time.ToString("HH:mm:ss"),
                $"[{stepColor}]{Markup.Escape(item.Step.ToString())}[/]",
                Markup.Escape(item.Message));
        }

        if (snapshot.Events.Count == 0)
        {
            events.AddRow("-", "-", "Waiting for events...");
        }

        return new Rows(
            new Panel(info).Header("Run Overview").RoundedBorder(),
            events);
    }
}
