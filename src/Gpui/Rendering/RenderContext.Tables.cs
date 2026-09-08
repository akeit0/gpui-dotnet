using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Declares a virtualized table. Rows render through the same coarse batch pipeline as
    /// <see cref="List"/>, while the declared columns drive the native header strip and the
    /// width/alignment of every <see cref="TableCell"/> inside each row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<TableTag> Table(
        ref ListController controller,
        ListDataSource dataSource,
        ListItemRenderer renderer,
        params ReadOnlySpan<TableColumn> columns
    ) => Table(ref controller, dataSource, renderer, default, columns);

    /// <summary>Declares a virtualized table bound to a controller-owned retained key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<TableTag> Table(
        ref ListController controller,
        ListDataSource dataSource,
        ListItemRenderer renderer,
        TableOptions options,
        params ReadOnlySpan<TableColumn> columns
    )
    {
        BindAutoController(ref controller);
        return TableCore(controller.Utf8KeySpan, dataSource, renderer, options, columns);
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
        ResourceKeys.ValidateExplicitChars(key, nameof(key));
        return TableCore(key, dataSource, renderer, options, columns);
    }

    private Element<TableTag> TableCore(
        ReadOnlySpan<byte> key,
        ListDataSource dataSource,
        ListItemRenderer renderer,
        TableOptions options,
        ReadOnlySpan<TableColumn> columns
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

        var element = ArenaWriter.AddTableNode<TableTag>(_arena, ComponentId.Table, key, columns);
        return ConfigureTable(element, dataSource, renderer, options, columns);
    }

    private Element<TableTag> TableCore(
        ReadOnlySpan<char> key,
        ListDataSource dataSource,
        ListItemRenderer renderer,
        TableOptions options,
        ReadOnlySpan<TableColumn> columns
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
        return ConfigureTable(element, dataSource, renderer, options, columns);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Element<TableTag> ConfigureTable(
        Element<TableTag> element,
        ListDataSource dataSource,
        ListItemRenderer renderer,
        TableOptions options,
        ReadOnlySpan<TableColumn> columns
    )
    {
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.ListItemCount, checked((uint)dataSource.Count));
        ArenaWriter.AddCallback(element.Inner, OpCode.ListRenderer, renderer.Token);
        // Same default-skipping contract as ListCore: options equal to their native defaults
        // are omitted from the arena.
        if (options.EffectiveBatchSize != 48)
        {
            ArenaWriter.AddU32(
                element.Inner,
                OpCode.ListBatchSize,
                checked((uint)options.EffectiveBatchSize)
            );
        }
        if (options.EffectiveOverdraw != 240)
        {
            ArenaWriter.AddF32(element.Inner, OpCode.ListOverdrawPx, options.EffectiveOverdraw);
        }
        if (options.EffectiveEstimatedItemExtent is { } extent)
        {
            ArenaWriter.AddF32(element.Inner, OpCode.ListEstimatedItemExtentPx, extent);
        }
        ArenaWriter.AddU64(element.Inner, OpCode.ListContentRevision, dataSource.ContentRevision);
        if (dataSource.ProjectionRevision is { } projection)
        {
            ArenaWriter.AddU64(element.Inner, OpCode.ListProjectionRevision, projection);
        }
        if (!options.EffectiveSmoothScrolling)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.SmoothScroll, 0);
        }
        if (!options.EffectiveShowScrollbar)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.ShowScrollbar, 0);
        }
        if (options.EffectiveScrollbarGutter)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.ScrollbarGutter, 1);
        }
        if (options.EffectiveScrollbarWidth != 8)
        {
            ArenaWriter.AddF32(
                element.Inner,
                OpCode.ScrollbarWidth,
                options.EffectiveScrollbarWidth
            );
        }
        if (!options.EffectiveShowHeader)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.TableShowHeader, 0);
        }
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
}
