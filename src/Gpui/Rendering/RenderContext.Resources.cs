using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Declares a retained native scroll container. Wheel/trackpad scrolling stays entirely in
    /// Rust; the resource is scoped by the current managed View handle plus <paramref name="key"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ScrollTag> Scroll(
        ReadOnlySpan<char> key,
        ScrollAxis axis = ScrollAxis.Vertical,
        params ReadOnlySpan<Element> children
    ) => Scroll(key, axis, default, children);

    /// <summary>
    /// Declares a vertical retained native scroll container with explicit scrolling behavior.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ScrollTag> Scroll(
        ReadOnlySpan<char> key,
        ScrollOptions options,
        params ReadOnlySpan<Element> children
    ) => Scroll(key, ScrollAxis.Vertical, options, children);

    /// <summary>
    /// Declares a retained native scroll container with explicit scrolling behavior.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ScrollTag> Scroll(
        ReadOnlySpan<char> key,
        ScrollAxis axis,
        ScrollOptions options,
        params ReadOnlySpan<Element> children
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A scroll resource key cannot be empty.", nameof(key));
        }
        if ((uint)axis > (uint)ScrollAxis.Both)
        {
            throw new ArgumentOutOfRangeException(nameof(axis));
        }

        var element = ArenaWriter.AddNode<ScrollTag>(_arena, ComponentId.Scroll, key);
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.ScrollAxis, (uint)axis);
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.SmoothScroll,
            options.EffectiveSmoothScrolling ? 1u : 0u
        );
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.ShowScrollbar,
            options.EffectiveShowScrollbar ? 1u : 0u
        );
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.ScrollbarGutter,
            options.EffectiveScrollbarGutter ? 1u : 0u
        );
        ArenaWriter.AddF32(element.Inner, OpCode.ScrollbarWidth, options.EffectiveScrollbarWidth);
        ArenaWriter.AddChildren(element.Inner, children);
        return element;
    }

    /// <summary>
    /// Declares a variable-height virtualized list. GPUI retains measurements and requests managed
    /// rows in coarse batches. The renderer must be generated from a [GpuiListItem] method.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ListTag> List(
        ReadOnlySpan<char> key,
        int itemCount,
        ListItemRenderer renderer,
        ListOptions options = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        return ListCore(key, itemCount, null, renderer, options);
    }

    /// <summary>
    /// Declares a variable-height virtualized list with explicit datasource content identity.
    /// Native row batches survive unrelated managed renders while the revision is unchanged.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ListTag> List(
        ReadOnlySpan<char> key,
        ListDataSource dataSource,
        ListItemRenderer renderer,
        ListOptions options = default
    ) => ListCore(key, dataSource.Count, dataSource.ContentRevision, renderer, options);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Element<ListTag> ListCore(
        ReadOnlySpan<char> key,
        int itemCount,
        ulong? contentRevision,
        ListItemRenderer renderer,
        ListOptions options
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A list resource key cannot be empty.", nameof(key));
        }
        if (renderer.IsDefault)
        {
            throw new ArgumentException("A generated list renderer is required.", nameof(renderer));
        }
        if ((uint)options.EffectiveAlignment > (uint)ListAlignment.Bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        var element = ArenaWriter.AddNode<ListTag>(_arena, ComponentId.List, key);
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.ListItemCount, checked((uint)itemCount));
        ArenaWriter.AddCallback(element.Inner, OpCode.ListRenderer, renderer.Token);
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.ListBatchSize,
            checked((uint)options.EffectiveBatchSize)
        );
        ArenaWriter.AddF32(element.Inner, OpCode.ListOverdrawPx, options.EffectiveOverdraw);
        ArenaWriter.AddU32(element.Inner, OpCode.ListAlignment, (uint)options.EffectiveAlignment);
        ArenaWriter.AddF32(
            element.Inner,
            OpCode.ListEstimatedItemHeightPx,
            options.EffectiveEstimatedItemHeight
        );
        if (contentRevision is { } revision)
        {
            ArenaWriter.AddU64(element.Inner, OpCode.ListContentRevision, revision);
        }
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.SmoothScroll,
            options.EffectiveSmoothScrolling ? 1u : 0u
        );
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.ShowScrollbar,
            options.EffectiveShowScrollbar ? 1u : 0u
        );
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.ScrollbarGutter,
            options.EffectiveScrollbarGutter ? 1u : 0u
        );
        ArenaWriter.AddF32(element.Inner, OpCode.ScrollbarWidth, options.EffectiveScrollbarWidth);
        return element;
    }

    /// <summary>
    /// Declares a virtualized table. Rows render through the same coarse batch pipeline as
    /// <see cref="List"/>, while the declared columns drive the native header strip and the
    /// width/alignment of every <see cref="TableCell"/> inside each row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<TableTag> Table(
        ReadOnlySpan<char> key,
        ListDataSource dataSource,
        ListItemRenderer renderer,
        params ReadOnlySpan<TableColumn> columns
    ) => Table(key, dataSource, renderer, default, columns);

    /// <summary>Declares a virtualized table with explicit options.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<TableTag> Table(
        ReadOnlySpan<char> key,
        ListDataSource dataSource,
        ListItemRenderer renderer,
        TableOptions options,
        params ReadOnlySpan<TableColumn> columns
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A table resource key cannot be empty.", nameof(key));
        }
        if (renderer.IsDefault)
        {
            throw new ArgumentException("A generated list renderer is required.", nameof(renderer));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(dataSource.Count);
        ValidateTableColumns(columns);

        var element = ArenaWriter.AddNode<TableTag>(
            _arena,
            ComponentId.Table,
            EncodeTableData(key, columns)
        );
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.ListItemCount, checked((uint)dataSource.Count));
        ArenaWriter.AddCallback(element.Inner, OpCode.ListRenderer, renderer.Token);
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.ListBatchSize,
            checked((uint)options.EffectiveBatchSize)
        );
        ArenaWriter.AddF32(element.Inner, OpCode.ListOverdrawPx, options.EffectiveOverdraw);
        ArenaWriter.AddF32(
            element.Inner,
            OpCode.ListEstimatedItemHeightPx,
            options.EffectiveEstimatedItemHeight
        );
        ArenaWriter.AddU64(element.Inner, OpCode.ListContentRevision, dataSource.ContentRevision);
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.SmoothScroll,
            options.EffectiveSmoothScrolling ? 1u : 0u
        );
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.ShowScrollbar,
            options.EffectiveShowScrollbar ? 1u : 0u
        );
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.ScrollbarGutter,
            options.EffectiveScrollbarGutter ? 1u : 0u
        );
        ArenaWriter.AddF32(element.Inner, OpCode.ScrollbarWidth, options.EffectiveScrollbarWidth);
        ArenaWriter.AddU32(
            element.Inner,
            OpCode.TableShowHeader,
            options.EffectiveShowHeader ? 1u : 0u
        );
        foreach (var column in columns)
        {
            ArenaWriter.AddU64(element.Inner, OpCode.TableColumn, PackTableColumn(column));
        }
        return element;
    }

    /// <summary>
    /// Declares one table row cell inside a [GpuiListItem] renderer. The cell's width and
    /// alignment are reconciled natively against the column declared at
    /// <paramref name="column"/>; declare content styling on the children. Cells compose as
    /// flex items: place them inside a horizontal container (for example
    /// <see cref="RenderContext.HStack"/>) stretched to the row width — plain divs are block
    /// by default and would stack them vertically.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<DivTag> TableCell(int column, params ReadOnlySpan<Element> children)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        var element = ArenaWriter.AddNode<DivTag>(_arena, ComponentId.Div);
        ArenaWriter.AddU32(element.Inner, OpCode.TableCellColumn, checked((uint)column));
        ArenaWriter.AddChildren(element.Inner, children);
        return element;
    }

    private static void ValidateTableColumns(ReadOnlySpan<TableColumn> columns)
    {
        for (var i = 0; i < columns.Length; i++)
        {
            var column = columns[i];
            if (string.IsNullOrEmpty(column.Key))
            {
                throw new ArgumentException($"Column {i} requires a key.", nameof(columns));
            }
            if (
                column.Key.Contains('\0')
                || column.Key.Contains('\u001F')
                || (column.Header?.Contains('\0') ?? false)
                || (column.Header?.Contains('\u001F') ?? false)
            )
            {
                throw new ArgumentException(
                    $"Column {i} key/header must not contain control characters.",
                    nameof(columns)
                );
            }
            if (!float.IsFinite(column.Width) || column.Width <= 0)
            {
                throw new ArgumentException(
                    $"Column {i} width must be finite and positive.",
                    nameof(columns)
                );
            }
            if (column.IsFraction && column.Width is <= 0f or > 1.0f)
            {
                throw new ArgumentException(
                    $"Column {i} is a fraction and must be in (0, 1].",
                    nameof(columns)
                );
            }
            if ((uint)column.Unit > (uint)TableColumnWidth.Fraction)
            {
                throw new ArgumentException(
                    $"Column {i} has an undefined {nameof(TableColumnWidth)}.",
                    nameof(columns)
                );
            }
            if ((uint)column.Alignment > (uint)TableColumnAlignment.Right)
            {
                throw new ArgumentException(
                    $"Column {i} has an undefined {nameof(TableColumnAlignment)}.",
                    nameof(columns)
                );
            }

            // Tables have few columns, so an allocation-free quadratic scan beats a HashSet.
            for (var j = 0; j < i; j++)
            {
                if (column.Key == columns[j].Key)
                {
                    throw new ArgumentException(
                        $"Column {i} repeats key '{column.Key}'; column keys must be unique.",
                        nameof(columns)
                    );
                }
            }
        }
    }

    /// <summary>
    /// Encodes the table's strings: the row-engine key followed by one NUL-separated
    /// key/header pair per column. Numeric column data travels as one packed
    /// <see cref="OpCode.TableColumn"/> op per column (see <see cref="PackTableColumn"/>).
    /// </summary>
    private static string EncodeTableData(ReadOnlySpan<char> key, ReadOnlySpan<TableColumn> columns)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(key);
        foreach (var column in columns)
        {
            builder.Append('\0').Append(column.Key).Append('\0').Append(column.Header);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Packs one column's numeric record: width f32 bits in the low word, unit in bits
    /// 32..34, alignment in bits 34..36. Must mirror the native <c>unpack_table_column</c>.
    /// </summary>
    private static ulong PackTableColumn(TableColumn column) =>
        BitConverter.SingleToUInt32Bits(column.Width)
        | ((ulong)(uint)column.Unit << 32)
        | ((ulong)(uint)column.Alignment << 34);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint CurrentResourceOwner()
    {
        var owner =
            _owner
            ?? throw new InvalidOperationException(
                "Retained native resources require a mounted managed View. Element-only RenderArenaOwner tests cannot create them."
            );
        var handle = owner.RuntimeViewHandle;
        return handle != 0
            ? handle
            : throw new InvalidOperationException("The owning View is not mounted.");
    }
}
