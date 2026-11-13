using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenTaan.ServerSwitcher.Editor.Views;

/// <summary>A small modal message box with custom buttons (Avalonia has no built-in one).</summary>
public sealed class MessageDialog : Window
{
    private MessageDialog(string title, string message, IReadOnlyList<string> buttons, int primaryIndex, int cancelIndex, bool isError)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = buttons[i],
                IsDefault = i == primaryIndex,
                IsCancel = i == cancelIndex,
                MinWidth = 88,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            if (i == primaryIndex)
            {
                button.Classes.Add("primary");
            }

            button.Click += (_, _) => Close(index);
            buttonRow.Children.Add(button);
        }

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = isError ? (IBrush)Application.Current!.FindResource("ErrorSoftBrush")! : (IBrush)Application.Current!.FindResource("TextBrush")!,
        };

        Content = new Border
        {
            Padding = new Thickness(24, 20),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    heading,
                    new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = (IBrush)Application.Current!.FindResource("TextSecondaryBrush")! },
                    buttonRow,
                },
            },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(cancelIndex);
            }
        };
    }

    /// <summary>Shows the dialog and returns the index of the clicked button (or <paramref name="cancelIndex"/> when closed).</summary>
    public static async Task<int> ShowAsync(Window owner, string title, string message, IReadOnlyList<string> buttons, int primaryIndex, int cancelIndex, bool isError = false)
    {
        var dialog = new MessageDialog(title, message, buttons, primaryIndex, cancelIndex, isError);
        var result = await dialog.ShowDialog<int?>(owner);
        return result ?? cancelIndex;
    }
}
