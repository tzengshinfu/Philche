namespace Philche.Core.Orchestration;

public enum TriggerEventKind
{
    AgentVersionChanged = 1,
    SkillsFolderChanged = 2,
    MainConfigChanged = 3,
    ExtensionListChanged = 4,
    RuleSetVersionChanged = 5,
    ContextMenuScan = 6,
}
