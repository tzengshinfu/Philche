using Philche.Core.Orchestration;
using Philche.Tray;

namespace Philche.Core.Test;

public sealed class ToastNotifierTests : IDisposable
{
    private readonly Func<bool> originalIsToastSupported;
    private readonly Func<string, bool> originalToastSender;
    private readonly Action<string> originalWarningLogger;

    public ToastNotifierTests()
    {
        originalIsToastSupported = ToastNotifier.IsToastSupported;
        originalToastSender = ToastNotifier.ToastSender;
        originalWarningLogger = ToastNotifier.WarningLogger;
    }

    [Fact]
    public void TryShow_WhenToastUnsupported_LogsWarningAndSkipsSender()
    {
        var senderCalled = false;
        var warnings = new List<string>();

        ToastNotifier.IsToastSupported = () => false;
        ToastNotifier.ToastSender = _ =>
        {
            senderCalled = true;
            return true;
        };
        ToastNotifier.WarningLogger = message => warnings.Add(message);

        ToastNotifier.TryShowModelMissing("模型不存在");

        Assert.False(senderCalled);
        Assert.Contains(warnings, warning => warning.Contains("not supported", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryShowModelMissing_IncludesOpenModelsProtocolAction()
    {
        string? capturedXml = null;
        ToastNotifier.IsToastSupported = () => true;
        ToastNotifier.ToastSender = xml =>
        {
            capturedXml = xml;
            return true;
        };
        ToastNotifier.WarningLogger = _ => { };

        ToastNotifier.TryShowModelMissing("缺少模型");

        Assert.NotNull(capturedXml);
        Assert.Contains("philche-open-models://open-models", capturedXml, StringComparison.Ordinal);
    }

    [Fact]
    public void TryShowScanCompleted_WithFindings_IncludesCountAndPath()
    {
        string? capturedXml = null;
        ToastNotifier.IsToastSupported = () => true;
        ToastNotifier.ToastSender = xml =>
        {
            capturedXml = xml;
            return true;
        };
        ToastNotifier.WarningLogger = _ => { };

        var progress = new ScanProgressInfo(
            ScanType: "Manual",
            AgentKey: "scan-1",
            Status: "Completed",
            FindingsCount: 3,
            HighRiskPaths: new[] { "skills/test-agent.yaml" },
            TotalFiles: 2,
            ScannedFiles: 2);

        ToastNotifier.TryShowScanCompleted(progress);

        Assert.NotNull(capturedXml);
        Assert.Contains("偵測到 3 筆風險", capturedXml, StringComparison.Ordinal);
        Assert.Contains("skills/test-agent.yaml", capturedXml, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        ToastNotifier.IsToastSupported = originalIsToastSupported;
        ToastNotifier.ToastSender = originalToastSender;
        ToastNotifier.WarningLogger = originalWarningLogger;
    }
}
