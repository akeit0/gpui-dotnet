namespace Gpui;

public static partial class ElementExtensions
{
    /// <summary>
    /// Supplies one header content element per declared column, in column order. Call once;
    /// omitting this declaration uses the column labels. Native column widths, alignment, and
    /// scrollbar gutter still apply. Header content uses normal View events and composition.
    /// </summary>
    public static Element<TableTag> Header(
        this Element<TableTag> table,
        params ReadOnlySpan<Element> cells
    )
    {
        ArenaWriter.AddChildren(table.Inner, cells);
        return table;
    }
}
