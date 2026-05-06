using Philche.Core.Domain.Models;

namespace Philche.Core.Discovery;

public static class KnownAgentCatalog
{
    public static IReadOnlyList<KnownAgentCatalogEntry> Entries { get; } =
    [
        new()
        {
            AgentKey = "github-copilot-cli",
            DisplayName = "GitHub Copilot CLI",
            HostExecutablePaths =
            [
                @"%LOCALAPPDATA%\Programs\GitHub Copilot\copilot.exe",
                @"%USERPROFILE%\.local\bin\copilot.exe"
            ],
            ExecutableNames = ["copilot.exe"],
            WslUserRelativePath = ".copilot/skills"
        },
        new()
        {
            AgentKey = "openai-codex-cli",
            DisplayName = "OpenAI Codex CLI",
            HostExecutablePaths =
            [
                @"%LOCALAPPDATA%\Programs\OpenAI Codex\codex.exe",
                @"%USERPROFILE%\.local\bin\codex.exe"
            ],
            ExecutableNames = ["codex.exe"],
            WslUserRelativePath = ".codex/skills"
        },
        new()
        {
            AgentKey = "google-gemini-cli",
            DisplayName = "Google Gemini CLI",
            HostExecutablePaths =
            [
                @"%LOCALAPPDATA%\Programs\Google\Gemini\gemini.exe",
                @"%USERPROFILE%\.local\bin\gemini.exe"
            ],
            ExecutableNames = ["gemini.exe"],
            WslUserRelativePath = ".gemini/skills"
        },
        new()
        {
            AgentKey = "openclaw",
            DisplayName = "OpenClaw",
            HostExecutablePaths =
            [
                @"%NVM_SYMLINK%\openclaw.cmd",
                @"%APPDATA%\npm\openclaw.cmd",
                @"%LOCALAPPDATA%\npm\openclaw.cmd"
            ],
            ExecutableNames = ["openclaw", "openclaw.cmd"],
            WslUserRelativePath = ".openclaw/workspace/skills"
        },
        new()
        {
            AgentKey = "claude-code",
            DisplayName = "Claude Code",
            WslUserRelativePath = ".claude/skills"
        },
        new()
        {
            AgentKey = "claude-cowork",
            DisplayName = "Claude Cowork",
            SkillsPaths = [new SkillsPathEntry(@"%USERPROFILE%\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\local-agent-mode-sessions")]
        }
    ];
}
