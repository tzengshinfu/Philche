namespace Philche.Core.Orchestration;

public static class ScanTriggerReason
{
    public const string Periodic = "periodic";
    public const string Manual = "manual";
    public const string AgentVersionChanged = "agent-version-changed";
    public const string SkillsFolderChanged = "skills-folder-changed";
    public const string MainConfigChanged = "main-config-changed";
    public const string ExtensionListChanged = "extension-list-changed";
    public const string RuleSetVersionChanged = "rule-set-version-changed";
    public const string ContextMenuScan = "context-menu";
}
