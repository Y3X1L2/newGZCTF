using System.ComponentModel.DataAnnotations;

namespace GZCTF.Infrastructure.Persistence.Governance;

public sealed class DataRetentionOptions
{
    public const string SectionName = "DataRetention";

    [Range(1, 3650)] public int SystemLogDays { get; set; } = 30;
    [Range(1, 3650)] public int TeamLabFlowDays { get; set; } = 7;
    [Range(1, 3650)] public int TeamLabFlowAggregateDays { get; set; } = 180;
    [Range(1, 3650)] public int DeploymentTicketDays { get; set; } = 180;
    [Range(1, 3650)] public int ApiOperationDays { get; set; } = 90;
    [Range(1, 3650)] public int TeamLabEventDays { get; set; } = 180;
    [Range(1, 3650)] public int GovernanceRunDays { get; set; } = 365;
    [Range(100, 20000)] public int DeleteBatchSize { get; set; } = 1000;
    [Range(1, 1440)] public int IntervalMinutes { get; set; } = 60;
    [Range(0, 3600)] public int StartupDelaySeconds { get; set; } = 90;
}
