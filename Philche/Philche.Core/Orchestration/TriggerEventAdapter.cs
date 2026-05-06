namespace Philche.Core.Orchestration;

public static class TriggerEventAdapter
{
    public static string ToReason(TriggerEventKind kind) => kind switch
    {
        TriggerEventKind.AgentVersionChanged => ScanTriggerReason.AgentVersionChanged,
        TriggerEventKind.SkillsFolderChanged => ScanTriggerReason.SkillsFolderChanged,
        TriggerEventKind.MainConfigChanged => ScanTriggerReason.MainConfigChanged,
        TriggerEventKind.ExtensionListChanged => ScanTriggerReason.ExtensionListChanged,
        TriggerEventKind.RuleSetVersionChanged => ScanTriggerReason.RuleSetVersionChanged,
        TriggerEventKind.ContextMenuScan => ScanTriggerReason.ContextMenuScan,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
