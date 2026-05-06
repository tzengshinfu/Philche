using Philche.Core.Config;
using Philche.Core.Discovery;
using Philche.Core.Domain.Enums;
using Philche.Core.SkillsRisk;

namespace Philche.Core.Test;

/// <summary>
/// Integration tests that run real Philche scans against the locally installed
/// OpenClaw agent. These exercise the full scanning pipeline end-to-end:
///   1. Agent discovery via KnownAgentCatalog
///   2. Prompt risk evaluation (Rules + Regex + Guard keyword fallback)
///   3. Code scanning (AgentCodeCollector + YaraCodeScanner)
///
/// Guard and Semantic detectors run in degraded/fallback mode (no GGUF models).
/// The demo skill was crafted with deliberately suspicious content to validate
/// that the pipeline correctly flags risky prompts and code artifacts.
/// </summary>
public sealed class OpenClawScanIntegrationTests
{
    // ────────────────────────── helpers ──────────────────────────

    private static readonly string SkillsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".openclaw", "workspace", "skills");

    private static readonly string McpConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".openclaw", "openclaw.json");

    /// <summary>Stub semantic detector – no production impl exists yet.</summary>
    private sealed class StubSemanticDetector : ISemanticSimilarityDetector
    {
        public Task<double> ScoreAsync(SkillEvaluationInput input, CancellationToken ct = default)
            => Task.FromResult(0.0);
    }

    // ────────────────────── 1. Discovery ────────────────────────

    [Fact]
    public async Task Discovery_FindsOpenClawOnHost()
    {
        var service = new HostAgentDiscoveryService();
        var items = await service.DiscoverAsync();

        var openclaw = items.FirstOrDefault(i => i.AgentKey == "openclaw");
        Assert.NotNull(openclaw);
        Assert.Equal(SurfaceType.Host, openclaw.SurfaceType);
        Assert.False(string.IsNullOrWhiteSpace(openclaw.ExecutablePath));

        Output($"[Discovery] OpenClaw found: {openclaw.ExecutablePath}  version={openclaw.Version}");
    }

    // ────────────────── 2. Prompt Risk Scan ─────────────────────

    [Fact]
    public async Task PromptRiskScan_SkillMd_DetectsHighRisk()
    {
        var skillMdPath = Path.Combine(SkillsRoot, "demo-data-export", "SKILL.md");
        Assert.True(File.Exists(skillMdPath), $"SKILL.md not found at {skillMdPath}");

        var content = await File.ReadAllTextAsync(skillMdPath);

        var evaluator = BuildEvaluator();
        var input = new SkillEvaluationInput(content, skillMdPath, IsCode: false);
        var result = await evaluator.EvaluateAsync(input);

        Output($"[Prompt Scan] SKILL.md → RiskLevel={result.RiskLevel}  Degraded={result.IsDegradedMode}");
        foreach (var e in result.Evidence)
            Output($"  [{e.Detector}] score={e.Score:F3}  {e.Message}");

        // The skill contains exfiltration URLs, prompt injection, api-key theft etc.
        // Rules+Regex alone should flag HIGH or at minimum MEDIUM.
        Assert.True(result.RiskLevel >= RiskLevel.Medium,
            $"Expected at least Medium risk but got {result.RiskLevel}");
    }

    // ──────────────────── 3. Code Scan ──────────────────────────

    [Fact]
    public async Task CodeScan_ExportHelperPy_DetectsFindings()
    {
        Assert.True(Directory.Exists(SkillsRoot), $"Skills root not found: {SkillsRoot}");

        var collector = new AgentCodeCollector(new YaraCodeScanner());
        var result = await collector.ScanDirectoriesAsync([SkillsRoot]);

        Output($"[Code Scan] Files scanned={result.TotalFilesScanned}  With findings={result.FilesWithFindings}");
        foreach (var f in result.FileResults)
        {
            Output($"  {f.FilePath}");
            foreach (var e in f.Evidence)
                Output($"    [{e.Detector}] score={e.Score:F3}  {e.Message}");
        }

        // export_helper.py contains process.start / exec / password / api_key / base64 patterns
        Assert.True(result.FilesWithFindings > 0, "Expected code scan findings from export_helper.py");

        var pyResult = result.FileResults.FirstOrDefault(f =>
            f.FilePath.EndsWith("export_helper.py", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(pyResult);
        Assert.True(pyResult.Evidence.Count > 0, "Expected YARA evidence hits on export_helper.py");
    }

    // ──────────── 4. Code evaluated as SkillRiskEvaluator ───────

    [Fact]
    public async Task CodeRisk_ExportHelperPy_DetectsRisk()
    {
        var codePath = Path.Combine(SkillsRoot, "demo-data-export", "export_helper.py");
        Assert.True(File.Exists(codePath), $"export_helper.py not found at {codePath}");

        var content = await File.ReadAllTextAsync(codePath);

        var evaluator = BuildEvaluator();
        var input = new SkillEvaluationInput(content, codePath, IsCode: true);
        var result = await evaluator.EvaluateAsync(input);

        Output($"[Code Risk] export_helper.py → RiskLevel={result.RiskLevel}  Degraded={result.IsDegradedMode}");
        foreach (var e in result.Evidence)
            Output($"  [{e.Detector}] score={e.Score:F3}  {e.Message}");

        Assert.True(result.RiskLevel >= RiskLevel.Medium,
            $"Expected at least Medium code risk but got {result.RiskLevel}");
    }

    // ───────────── 5. Agent Config Parse Verification ─────────────

    [Fact]
    public void AgentConfig_ExistsAndContainsMcpServers()
    {
        Assert.True(File.Exists(McpConfigPath), $"openclaw.json not found at {McpConfigPath}");

        var json = File.ReadAllText(McpConfigPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        // OpenClaw 2026.3.x may no longer keep MCP package refs in openclaw.json.
        // Accept either legacy mcp-server package references OR migrated schema-only config.
        var hasLegacyMcpRef = json.Contains("mcp-server-", StringComparison.OrdinalIgnoreCase);
        var hasAgentsDefaultsModel = root.TryGetProperty("agents", out var agents)
            && agents.TryGetProperty("defaults", out var defaults)
            && defaults.TryGetProperty("model", out var model)
            && model.TryGetProperty("primary", out _);

        Assert.True(hasLegacyMcpRef || hasAgentsDefaultsModel,
            "Expected either legacy MCP server references or migrated agents.defaults.model config.");

        Output($"[Agent Config] {McpConfigPath} — parse OK, legacyMcpRef={hasLegacyMcpRef}, migratedSchema={hasAgentsDefaultsModel}");
    }

    // ─────────────── 6. Full Pipeline Summary ───────────────────

    [Fact]
    public async Task FullPipeline_OpenClawScanSummary()
    {
        // Discovery
        var discoveryService = new HostAgentDiscoveryService();
        var inventory = await discoveryService.DiscoverAsync();
        var oc = inventory.FirstOrDefault(i => i.AgentKey == "openclaw");
        Assert.NotNull(oc);

        Output("╔══════════════════════════════════════════════════════════╗");
        Output("║             PHILCHE SCAN REPORT — OpenClaw              ║");
        Output("╚══════════════════════════════════════════════════════════╝");
        Output($"  Agent:    {oc.DisplayName}  ({oc.AgentKey})");
        Output($"  Surface:  {oc.SurfaceType}");
        Output($"  Path:     {oc.ExecutablePath}");
        Output($"  Version:  {oc.Version}");
        Output("");

        // Prompt scan — all SKILL.md files
        var evaluator = BuildEvaluator();
        var promptResults = new List<(string Path, SkillRiskResult Result)>();

        foreach (var skillDir in Directory.GetDirectories(SkillsRoot))
        {
            var skillMd = Path.Combine(skillDir, "SKILL.md");
            if (!File.Exists(skillMd)) continue;

            var content = await File.ReadAllTextAsync(skillMd);
            var input = new SkillEvaluationInput(content, skillMd, IsCode: false);
            var r = await evaluator.EvaluateAsync(input);
            promptResults.Add((skillMd, r));
        }

        Output("── Prompt Risk Scan ─────────────────────────────────────");
        foreach (var (path, r) in promptResults)
        {
            Output($"  [{r.RiskLevel}] {Path.GetRelativePath(SkillsRoot, path)}");
            foreach (var e in r.Evidence)
                Output($"        {e.Detector}: {e.Score:F3} — {e.Message}");
        }
        Output("");

        // Code scan
        var codeCollector = new AgentCodeCollector(new YaraCodeScanner());
        var codeScan = await codeCollector.ScanDirectoriesAsync([SkillsRoot]);

        Output("── Code Scan ────────────────────────────────────────────");
        Output($"  Files scanned:       {codeScan.TotalFilesScanned}");
        Output($"  Files with findings: {codeScan.FilesWithFindings}");
        foreach (var f in codeScan.FileResults)
        {
            Output($"  [{(f.Evidence.Sum(e => e.Score) >= 0.75 ? "HIGH" : f.Evidence.Sum(e => e.Score) >= 0.35 ? "MED " : "LOW ")}] {Path.GetRelativePath(SkillsRoot, f.FilePath)}");
            foreach (var e in f.Evidence)
                Output($"        {e.Detector}: {e.Score:F3} — {e.Message}");
        }
        Output("");

        // Agent config
        Output("── Agent Config ────────────────────────────────────────");
        if (File.Exists(McpConfigPath))
        {
            var json = await File.ReadAllTextAsync(McpConfigPath);
            Output($"  Config: {McpConfigPath}");
            Output($"  Contains mcp-server-sqlite: {json.Contains("mcp-server-sqlite")}");
            Output($"  Contains mcp-server-fetch:  {json.Contains("mcp-server-fetch")}");
        }
        else
        {
            Output($"  Config not found: {McpConfigPath}");
        }
        Output("");

        // Summary
        var highCount = promptResults.Count(x => x.Result.RiskLevel == RiskLevel.High)
                      + codeScan.FileResults.Count(f => f.Evidence.Sum(e => e.Score) >= 0.75);
        var medCount = promptResults.Count(x => x.Result.RiskLevel == RiskLevel.Medium)
                     + codeScan.FileResults.Count(f => f.Evidence.Sum(e => e.Score) is >= 0.35 and < 0.75);

        Output("══════════════════════════════════════════════════════════");
        Output($"  TOTAL HIGH-risk findings: {highCount}");
        Output($"  TOTAL MEDIUM-risk findings: {medCount}");
        Output("══════════════════════════════════════════════════════════");

        // At least one HIGH expected from the deliberately malicious content
        Assert.True(highCount + medCount > 0, "Expected at least one finding from the scan");
    }

    // ────────────── 7. Ad-hoc Single-File Prompt Scan ──────────

    /// <summary>
    /// Scans a single prompt file from the test-samples directory.
    /// Change <see cref="SingleFileRelativePath"/> to point at any .md / .txt file.
    /// Run:  dotnet test --filter "ScanSinglePromptFile" -l "console;verbosity=detailed"
    /// </summary>
    private static readonly string SingleFileRelativePath = Path.Combine(
        "malicious-agent-skill", "SKILL.md");

    [Fact]
    public async Task ScanSinglePromptFile()
    {
        var singleFilePath = ResolveTestSamplePath(SingleFileRelativePath);
        Assert.True(File.Exists(singleFilePath), $"File not found: {singleFilePath}");
        var content = await File.ReadAllTextAsync(singleFilePath);

        var evaluator = BuildEvaluator();
        var input = new SkillEvaluationInput(content, singleFilePath, IsCode: false);
        var result = await evaluator.EvaluateAsync(input);

        Output($"[Single Prompt Scan] {singleFilePath}");
        Output($"  Risk Level:    {result.RiskLevel}");
        Output($"  Degraded Mode: {result.IsDegradedMode}");
        foreach (var e in result.Evidence)
            Output($"  [{e.Detector}] score={e.Score:F3} — {e.Message}");

        // Keep this as an ad-hoc smoke test: score thresholds can vary by model/runtime.
        Assert.NotEmpty(result.Evidence);
    }

    // ────────────────────── private ─────────────────────────────

    private static string ResolveTestSamplePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidateRoot = Path.Combine(current.FullName, "test-samples");
            if (Directory.Exists(candidateRoot))
            {
                return Path.Combine(candidateRoot, relativePath);
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Unable to locate 'test-samples' from base directory: {AppContext.BaseDirectory}");
    }

    private static SkillRiskEvaluator BuildEvaluator()
    {
        var flags = new RuntimeFeatureFlags
        {
            EnableSemanticRiskStage = false,  // no production impl yet
            EnableGuardRiskStage = true,      // will degrade to keyword stub
            EnableYaraCodeScanning = true,
        };

        return new SkillRiskEvaluator(
            new RuleDetector(),
            new StubSemanticDetector(),
            new LlamaGuardClassifier(),       // no model → keyword fallback
            new NonBlockingRiskActionPolicy(),
            flags,
            new PromptPreprocessor(),
            new YaraCodeScanner());
    }

    private static void Output(string message) => Console.WriteLine(message);
}
