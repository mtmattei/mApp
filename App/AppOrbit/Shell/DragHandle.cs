using Microsoft.UI.Input;

namespace AppOrbit;

/// <summary>A Grid that shows a drag cursor: the viewer's head bar (the prototype's cursor: grab).</summary>
public sealed partial class DragHandle : Grid
{
    public DragHandle()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
    }
}
