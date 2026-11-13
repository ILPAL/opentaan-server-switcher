using OpenTaan.ServerSwitcher.Core.Editing;
using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Editor.ViewModels;

/// <summary>What child view models may ask of the main window view model.</summary>
public interface IEditorHost
{
    ConfigDocument? Document { get; }

    DocumentState State { get; }

    /// <summary>Runs an undoable edit; all views refresh afterwards.</summary>
    void Execute(IEditCommand command);

    /// <summary>Selects a node in the tree (expanding its ancestors) and shows it in the inspector.</summary>
    void Select(ConfigNode node);
}
