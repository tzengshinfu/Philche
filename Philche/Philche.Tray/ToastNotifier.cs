using System.Diagnostics;
using CommunityToolkit.WinUI.Notifications;
using Philche.Core.Orchestration;

namespace Philche.Tray;

internal static class ToastNotifier
{
    public const string OpenModelsAction = "open-models";
    private static Action<string>? activationCallback;
    internal static Func<bool> IsToastSupported { get; set; } = static () => OperatingSystem.IsWindows();
    internal static Func<string, bool> ToastSender { get; set; } = static xml =>
    {
        ShowToastXml(xml);
        return true;
    };

    internal static Action<string> WarningLogger { get; set; } = static message => Debug.WriteLine(message);

    public static void Initialize(Action<string> onAction)
    {
        activationCallback = onAction;
    }

    public static void InvokeAction(string action)
    {
        activationCallback?.Invoke(action);
    }

    public static void TryShowAlreadyRunning()
    {
        TryShow("Philche is already running", null, includeOpenSettingsAction: false);
    }

    public static void TryShowModelMissing(string message)
    {
        TryShow("Philche 模型檢查", message, includeOpenSettingsAction: true);
    }

    public static void TryShowScanCompleted(ScanProgressInfo progress)
    {
        if (!progress.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (progress.FindingsCount <= 0)
        {
            TryShow("Philche 掃描完成", "未發現風險", includeOpenSettingsAction: false);
            return;
        }

        var firstPath = progress.HighRiskPaths.FirstOrDefault();
        var pathText = string.IsNullOrWhiteSpace(firstPath) ? string.Empty : $"；路徑: {firstPath}";
        TryShow("Philche 掃描完成", $"偵測到 {progress.FindingsCount} 筆風險{pathText}", includeOpenSettingsAction: false);
    }

    public static void TryShow(string title, string? message, bool includeOpenSettingsAction)
    {
        if (!IsToastSupported())
        {
            WarningLogger("Toast notification is not supported on this OS. Skipping notification.");
            return;
        }

        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title);

            if (!string.IsNullOrWhiteSpace(message))
            {
                builder.AddText(message);
            }

            if (includeOpenSettingsAction)
            {
                builder.AddButton(new ToastButton()
                    .SetContent("開啟設定")
                    .SetProtocolActivation(new Uri("philche-open-models://open-models")));
            }

            var toastXml = builder.GetToastContent().GetContent();
            if (!ToastSender(toastXml))
            {
                WarningLogger("Toast notification was not shown by sender.");
            }
        }
        catch (Exception ex)
        {
            WarningLogger($"Failed to show toast notification: {ex.Message}");
            TryShowFallback(title, message);
        }
    }

    private static void ShowToastXml(string xmlContent)
    {
        var safeXml = xmlContent.Replace("'", "''", StringComparison.Ordinal);
        var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] > $null
$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$xml.LoadXml('{safeXml}')
$toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Philche.Tray').Show($toast)
";

        var escapedScript = script.Replace("\"", "\"\"").Replace(Environment.NewLine, "; ");
        var info = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{escapedScript}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        _ = Process.Start(info);
    }

    private static void TryShowFallback(string title, string? message)
    {
        try
        {
            var safeTitle = EscapeXml(title);
            var safeMessage = EscapeXml(message);
            var messageXml = string.IsNullOrWhiteSpace(safeMessage)
                ? string.Empty
                : $"<text>{safeMessage}</text>";

            var fallbackXml = $"<toast><visual><binding template=\"ToastGeneric\"><text>{safeTitle}</text>{messageXml}</binding></visual></toast>";
            if (!ToastSender(fallbackXml))
            {
                WarningLogger("Toast fallback notification was not shown by sender.");
            }
        }
        catch (Exception ex)
        {
            WarningLogger($"Failed to show fallback toast notification: {ex.Message}");
        }
    }

    private static string EscapeXml(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }
}