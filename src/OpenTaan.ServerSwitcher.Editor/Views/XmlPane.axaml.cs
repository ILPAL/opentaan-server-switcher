using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using OpenTaan.ServerSwitcher.Editor.ViewModels;

namespace OpenTaan.ServerSwitcher.Editor.Views;

public partial class XmlPane : UserControl
{
    private MainWindowViewModel? _viewModel;

    public XmlPane()
    {
        InitializeComponent();
        Editor.SyntaxHighlighting = CreateXmlHighlighting();
        Editor.TextArea.SelectionBrush = new SolidColorBrush(Color.Parse("#3D6CB0F5"));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Editor.Text = _viewModel.XmlText;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.XmlText) && _viewModel is not null)
        {
            // Keep the scroll position when the text is regenerated after an edit.
            var offset = Editor.TextArea.TextView.ScrollOffset;
            Editor.Text = _viewModel.XmlText;
            Editor.ScrollToVerticalOffset(offset.Y);
            Editor.ScrollToHorizontalOffset(offset.X);
        }
    }

    /// <summary>Built-in XML highlighting, recoloured to the editor palette (tag blue, attribute amber, value green).</summary>
    private static IHighlightingDefinition? CreateXmlHighlighting()
    {
        var definition = HighlightingManager.Instance.GetDefinition("XML");
        if (definition is null)
        {
            return null;
        }

        void Set(string name, string color)
        {
            if (definition.GetNamedColor(name) is { } named)
            {
                named.Foreground = new SimpleHighlightingBrush(Color.Parse(color));
            }
        }

        Set("XmlTag", "#7AB7F2");
        Set("AttributeName", "#E9B25C");
        Set("AttributeValue", "#9FD38A");
        Set("XmlDeclaration", "#7D8590");
        Set("Comment", "#7D8590");
        Set("Entity", "#C3A6F5");
        Set("CData", "#C3C9D1");
        Set("DocType", "#7D8590");
        return definition;
    }
}
