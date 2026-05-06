using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Philche.Tray;

internal static class ConfirmDialog
{
    public static async Task<bool> ShowAsync(Window owner, string title, string message, string confirmText = "Confirm", string cancelText = "Cancel")
    {
        var confirmed = false;

        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var confirmButton = new Button
        {
            Content = confirmText,
            Width = 130
        };

        var cancelButton = new Button
        {
            Content = cancelText,
            Width = 90
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                confirmButton
            }
        };

        var dialogContent = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(16),
            Children =
            {
                messageText,
                buttonPanel
            }
        };

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 150,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = dialogContent
        };

        confirmButton.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };

        cancelButton.Click += (_, _) =>
        {
            confirmed = false;
            dialog.Close();
        };

        await dialog.ShowDialog(owner);
        return confirmed;
    }
}
