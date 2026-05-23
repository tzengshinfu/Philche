using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Philche.Tray;

internal sealed class GuardModelDownloadDialog : Window
{
    private readonly TextBlock statusTextBlock;
    private readonly ProgressBar progressBar;

    public GuardModelDownloadDialog(string title)
    {
        Title = title;
        Width = 520;
        Height = 170;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        statusTextBlock = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = string.Empty,
        };

        progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 14,
            Value = 0,
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                statusTextBlock,
                progressBar,
            },
        };
    }

    public void UpdateStatus(string status)
    {
        statusTextBlock.Text = status;
    }

    public void UpdateProgress(double percentage)
    {
        progressBar.Value = Math.Clamp(percentage, 0, 100);
    }
}