namespace Unload.Tasks;

/// <summary>Режим интерпретации входных кодов запуска.</summary>
public enum RunSelectionMode
{
    MemberCodes,
    TargetCodes
}

/// <summary>
/// Единый запрос запуска задачи выгрузки.
/// </summary>
/// <param name="TaskCode">Код запускаемой задачи.</param>
/// <param name="AdminOverride">Пропустить проверки ограничений запуска.</param>
/// <param name="PublishToGateway">Публиковать результат в Gateway.</param>
/// <param name="Codes">Входные коды выбора (только для main-выгрузки).</param>
/// <param name="SelectionMode">Режим интерпретации кодов (только для main-выгрузки).</param>
/// <param name="SelectedBanks">
/// Выбранные банки для extra-выгрузки. Пусто/<c>null</c> — все банки (базовые скрипты);
/// непустой набор — atomic-скрипты с фильтром по этим банкам.
/// </param>
public record TaskLaunchRequest(
    string TaskCode,
    bool AdminOverride = false,
    bool PublishToGateway = true,
    IReadOnlyCollection<string>? Codes = null,
    RunSelectionMode SelectionMode = RunSelectionMode.MemberCodes,
    IReadOnlyCollection<string>? SelectedBanks = null);
