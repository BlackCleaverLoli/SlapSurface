namespace SlapSurface;

/// <summary>
/// Per-frame record of the first item row that captured hover, taken by the host
/// once per frame. Deliberately outside <see cref="HoverArbitration.Reset"/>,
/// which runs once per window frame and would drop a sibling window's result.
/// </summary>
internal static class SlapItemTooltip
{
    private static uint _itemId;

    public static void Request(bool hovered, uint itemId)
    {
        if (!hovered || itemId == 0 || _itemId != 0)
            return;

        _itemId = itemId;
    }

    public static uint Take()
    {
        var itemId = _itemId;
        _itemId = 0;
        return itemId;
    }
}
