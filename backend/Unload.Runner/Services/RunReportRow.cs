namespace Unload.Runner;

internal record RunReportRow(
    string MemberName,
    string FileType,
    int FirstCodeDigit,
    string OutputFileName,
    int RowsCount,
    bool MqSent,
    long ExecutionTimeMs);

