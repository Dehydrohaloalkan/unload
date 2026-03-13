using Spectre.Console;

namespace Unload.WebConsole;

/// <summary>
/// Строит визуальное представление текущего состояния запуска в консоли.
/// </summary>
internal static class RunDashboardBuilder
{
    private const int MaxVisibleEvents = 10;
    private const int MaxVisibleMembers = 12;
    private const int MemberNameWidth = 24;
    private const int MessageWidth = 56;
    private static readonly Spinner WaitingSpinner = Spinner.Known.Dots;

    /// <summary>
    /// Формирует набор панелей для live-рендера статуса и последних событий.
    /// </summary>
    /// <param name="snapshot">Снимок состояния UI.</param>
    /// <param name="correlationId">Идентификатор отслеживаемого запуска.</param>
    /// <returns>Набор строк для отрисовки через Spectre.Console.</returns>
    public static Rows Build(UiSnapshot snapshot, string correlationId)
    {
        var spinner = GetSpinnerFrame();
        var statusText = snapshot.Status == RunLifecycleStatus.Running
            ? $"{spinner} {snapshot.Status}"
            : snapshot.Status.ToString();
        return BuildLayout(
            snapshot,
            correlationId,
            headerTitle: "Run Overview",
            statusLabel: "Status",
            statusText,
            eventsTitle: "Recent Events",
            membersTitle: "Members",
            emptyEventsMessage: $"{spinner} Waiting for events...",
            emptyMembersMessage: $"{spinner} Waiting for member statuses...");
    }

    /// <summary>
    /// Формирует финальный (статичный) snapshot после завершения запуска.
    /// </summary>
    /// <param name="snapshot">Снимок состояния UI.</param>
    /// <param name="correlationId">Идентификатор отслеживаемого запуска.</param>
    /// <returns>Финальный набор строк для отрисовки через Spectre.Console.</returns>
    public static Rows BuildFinal(UiSnapshot snapshot, string correlationId)
    {
        return BuildLayout(
            snapshot,
            correlationId,
            headerTitle: "Run Finished",
            statusLabel: "Final status",
            statusText: snapshot.Status.ToString(),
            eventsTitle: "Final Events",
            membersTitle: "Final Members",
            emptyEventsMessage: "No events.",
            emptyMembersMessage: "No member statuses.");
    }

    /// <summary>
    /// Формирует общий layout для live и final режимов.
    /// </summary>
    /// <param name="snapshot">Снимок состояния UI.</param>
    /// <param name="correlationId">Идентификатор отслеживаемого запуска.</param>
    /// <param name="headerTitle">Заголовок обзорной панели.</param>
    /// <param name="statusLabel">Подпись поля со статусом запуска.</param>
    /// <param name="statusText">Значение поля статуса запуска.</param>
    /// <param name="eventsTitle">Заголовок таблицы событий.</param>
    /// <param name="membersTitle">Заголовок таблицы мемберов.</param>
    /// <param name="emptyEventsMessage">Текст пустого состояния событий.</param>
    /// <param name="emptyMembersMessage">Текст пустого состояния мемберов.</param>
    /// <returns>Набор строк для отрисовки через Spectre.Console.</returns>
    private static Rows BuildLayout(
        UiSnapshot snapshot,
        string correlationId,
        string headerTitle,
        string statusLabel,
        string statusText,
        string eventsTitle,
        string membersTitle,
        string emptyEventsMessage,
        string emptyMembersMessage)
    {
        var info = BuildInfoPanel(snapshot, correlationId, headerTitle, statusLabel, statusText);
        var members = BuildMembersTable(snapshot, membersTitle, emptyMembersMessage);
        var events = BuildEventsTable(snapshot, eventsTitle, emptyEventsMessage);
        return new Rows(info, members, events);
    }

    /// <summary>
    /// Формирует обзорную панель запуска.
    /// </summary>
    /// <param name="snapshot">Снимок состояния UI.</param>
    /// <param name="correlationId">Идентификатор отслеживаемого запуска.</param>
    /// <param name="headerTitle">Заголовок панели.</param>
    /// <param name="statusLabel">Подпись поля статуса.</param>
    /// <param name="statusText">Значение поля статуса.</param>
    /// <returns>Готовая панель со статусом запуска.</returns>
    private static Panel BuildInfoPanel(
        UiSnapshot snapshot,
        string correlationId,
        string headerTitle,
        string statusLabel,
        string statusText)
    {
        var info = new Grid();
        info.AddColumn();
        info.AddColumn();
        info.AddRow("[grey]CorrelationId[/]", Markup.Escape(correlationId));
        info.AddRow("[grey]" + statusLabel + "[/]", $"[{GetRunStatusColor(snapshot.Status)}]{Markup.Escape(statusText)}[/]");
        info.AddRow("[grey]Last step[/]", Markup.Escape(snapshot.LastStep?.ToString() ?? "-"));
        info.AddRow("[grey]Updated[/]", snapshot.UpdatedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "-");
        var presetStateText = snapshot.PresetState is null
            ? "-"
            : snapshot.PresetState.PresetCompleted
                ? "completed"
                : snapshot.PresetState.ReadyForPreset
                    ? "ready"
                    : snapshot.PresetState.RequiresPresetExecution
                        ? "locked"
                        : "open";
        info.AddRow("[grey]Preset gate[/]", Markup.Escape(presetStateText));
        info.AddRow("[grey]Message[/]", Markup.Escape(snapshot.Message ?? "-"));
        return new Panel(info).Header(headerTitle).RoundedBorder();
    }

    /// <summary>
    /// Формирует таблицу статусов мемберов.
    /// </summary>
    /// <param name="snapshot">Снимок состояния UI.</param>
    /// <param name="title">Заголовок таблицы.</param>
    /// <param name="emptyMessage">Текст пустого состояния.</param>
    /// <returns>Готовая таблица статусов мемберов.</returns>
    private static Table BuildMembersTable(UiSnapshot snapshot, string title, string emptyMessage)
    {
        var members = new Table().Border(TableBorder.Rounded).Title(title);
        members.AddColumn("Member");
        members.AddColumn("Status");
        members.AddColumn("Step");
        members.AddColumn("Message");

        foreach (var member in snapshot.Members.Take(MaxVisibleMembers))
        {
            members.AddRow(
                Markup.Escape(TrimCell(member.MemberName, MemberNameWidth)),
                $"[{GetMemberStatusColor(member.Status)}]{Markup.Escape(member.Status.ToString())}[/]",
                Markup.Escape(member.LastStep?.ToString() ?? "-"),
                Markup.Escape(TrimCell(member.Message ?? "-", MessageWidth)));
        }

        if (snapshot.Members.Count == 0)
        {
            members.AddRow("-", "-", "-", emptyMessage);
        }
        else if (snapshot.Members.Count > MaxVisibleMembers)
        {
            members.AddRow("...", "...", "...", $"Hidden {snapshot.Members.Count - MaxVisibleMembers} members");
        }

        return members;
    }

    /// <summary>
    /// Формирует таблицу последних событий запуска.
    /// </summary>
    /// <param name="snapshot">Снимок состояния UI.</param>
    /// <param name="title">Заголовок таблицы.</param>
    /// <param name="emptyMessage">Текст пустого состояния.</param>
    /// <returns>Готовая таблица событий.</returns>
    private static Table BuildEventsTable(UiSnapshot snapshot, string title, string emptyMessage)
    {
        var events = new Table().Border(TableBorder.Rounded).Title(title);
        events.AddColumn("Time");
        events.AddColumn("Step");
        events.AddColumn("Message");

        foreach (var item in snapshot.Events.TakeLast(MaxVisibleEvents))
        {
            events.AddRow(
                item.Time.ToString("HH:mm:ss"),
                $"[{GetStepColor(item.Step)}]{Markup.Escape(item.Step.ToString())}[/]",
                Markup.Escape(TrimCell(item.Message, MessageWidth)));
        }

        if (snapshot.Events.Count == 0)
        {
            events.AddRow("-", "-", emptyMessage);
        }
        else if (snapshot.Events.Count > MaxVisibleEvents)
        {
            events.AddRow(
                "...",
                "...",
                $"Hidden {snapshot.Events.Count - MaxVisibleEvents} older events");
        }

        return events;
    }

    /// <summary>
    /// Возвращает цвет для статуса запуска.
    /// </summary>
    /// <param name="status">Статус запуска.</param>
    /// <returns>Имя цвета Spectre.Console.</returns>
    private static string GetRunStatusColor(RunLifecycleStatus status)
    {
        return status switch
        {
            RunLifecycleStatus.Completed => "green",
            RunLifecycleStatus.Failed => "red",
            RunLifecycleStatus.Running => "deepskyblue1",
            _ => "yellow"
        };
    }

    /// <summary>
    /// Возвращает цвет для статуса мембера.
    /// </summary>
    /// <param name="status">Статус мембера.</param>
    /// <returns>Имя цвета Spectre.Console.</returns>
    private static string GetMemberStatusColor(MemberRunLifecycleStatus status)
    {
        return status switch
        {
            MemberRunLifecycleStatus.Completed => "green",
            MemberRunLifecycleStatus.Failed => "red",
            MemberRunLifecycleStatus.Cancelled => "yellow",
            MemberRunLifecycleStatus.Running => "deepskyblue1",
            _ => "grey"
        };
    }

    /// <summary>
    /// Возвращает цвет для шага события раннера.
    /// </summary>
    /// <param name="step">Шаг пайплайна раннера.</param>
    /// <returns>Имя цвета Spectre.Console.</returns>
    private static string GetStepColor(RunnerStep step)
    {
        return step switch
        {
            RunnerStep.Failed => "red",
            RunnerStep.Completed => "green",
            RunnerStep.FileWritten => "deepskyblue1",
            _ => "grey"
        };
    }

    /// <summary>
    /// Обрезает длинный текст для размещения в ячейке таблицы.
    /// </summary>
    /// <param name="value">Исходное значение.</param>
    /// <param name="maxLength">Максимальная длина значения.</param>
    /// <returns>Обрезанное значение с суффиксом <c>...</c> при необходимости.</returns>
    private static string TrimCell(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength - 1), "...");
    }

    /// <summary>
    /// Возвращает текущий кадр встроенного спиннера ожидания.
    /// </summary>
    /// <returns>Текстовый кадр анимации.</returns>
    private static string GetSpinnerFrame()
    {
        var frames = WaitingSpinner.Frames;
        if (frames.Count == 0)
        {
            return ".";
        }

        var intervalMs = Math.Max(10L, (long)WaitingSpinner.Interval.TotalMilliseconds);
        var index = (int)((Environment.TickCount64 / intervalMs) % frames.Count);
        return frames[index];
    }
}
