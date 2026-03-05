using Spectre.Console;

namespace Unload.WebConsole;

/// <summary>
/// Строит визуальное представление текущего состояния запуска в консоли.
/// </summary>
internal static class RunDashboardBuilder
{
    /// <summary>
    /// Формирует набор панелей для live-рендера статуса и последних событий.
    /// </summary>
    /// <param name="snapshot">Снимок состояния UI.</param>
    /// <param name="correlationId">Идентификатор отслеживаемого запуска.</param>
    /// <returns>Набор строк для отрисовки через Spectre.Console.</returns>
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

        var members = new Table().Border(TableBorder.Rounded).Title("Members");
        members.AddColumn("Member");
        members.AddColumn("Status");
        members.AddColumn("Step");
        members.AddColumn("Message");

        foreach (var member in snapshot.Members)
        {
            var memberColor = member.Status switch
            {
                MemberRunLifecycleStatus.Completed => "green",
                MemberRunLifecycleStatus.Failed => "red",
                MemberRunLifecycleStatus.Cancelled => "yellow",
                MemberRunLifecycleStatus.Running => "deepskyblue1",
                _ => "grey"
            };
            members.AddRow(
                Markup.Escape(member.MemberName),
                $"[{memberColor}]{Markup.Escape(member.Status.ToString())}[/]",
                Markup.Escape(member.LastStep?.ToString() ?? "-"),
                Markup.Escape(member.Message ?? "-"));
        }

        if (snapshot.Members.Count == 0)
        {
            members.AddRow("-", "-", "-", "No member statuses yet.");
        }

        return new Rows(
            new Panel(info).Header("Run Overview").RoundedBorder(),
            members,
            events);
    }
}
