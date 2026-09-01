using System.Buffers;
using System.Runtime.CompilerServices;
using Gpui.Interop;
using Utf8StringInterpolation;

namespace Gpui;

/// <summary>
/// Compiler-facing interpolated string handler that formats values directly into a render arena.
/// Application code normally creates this implicitly through <c>ui.Text($"...")</c>.
/// </summary>
/// <remarks>
/// This is a <c>ref struct</c> handler consumed by the C# interpolated string lowering.
/// It is instantiated by the compiler when a <see cref="RenderContext"/>-scoped
/// interpolated string is passed to <see cref="RenderContext.Text(ref Utf8InterpolatedStringHandler)"/>.
/// Direct construction is not intended for application code.
/// The handler writes UTF-8 bytes into the current <see cref="RenderContext"/>'s
/// native arena without intermediate string allocations.
/// </remarks>
[InterpolatedStringHandler]
public unsafe ref struct Utf8InterpolatedStringHandler
{
    private readonly RenderArena* _arena;
    private readonly uint _offset;
    private Utf8StringWriter<ArenaUtf8BufferWriter> _writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Utf8InterpolatedStringHandler"/> for the given render context.
    /// </summary>
    /// <param name="literalLength">The total length of literal fragments in the interpolated string.</param>
    /// <param name="formattedCount">The number of formatted holes in the interpolated string.</param>
    /// <param name="context">The render context whose UTF-8 arena receives the formatted output.</param>
    /// <remarks>
    /// This constructor is called by the compiler-generated code for an interpolated string handler.
    /// The <paramref name="literalLength"/> and <paramref name="formattedCount"/> parameters are
    /// supplied by the compiler to allow upfront sizing of the internal writer.
    /// </remarks>
    public Utf8InterpolatedStringHandler(
        int literalLength,
        int formattedCount,
        RenderContext context
    )
    {
        _arena = context.NativeArena;
        _offset = checked((uint)_arena->Utf8Length);
        _writer = new Utf8StringWriter<ArenaUtf8BufferWriter>(
            literalLength,
            formattedCount,
            new ArenaUtf8BufferWriter(_arena)
        );
    }

    /// <summary>Appends a literal string fragment to the handler.</summary>
    /// <param name="value">The literal text to append. Must not be <c>null</c>.</param>
    public void AppendLiteral(string value) => _writer.AppendLiteral(value);

    /// <summary>Appends a formatted <see cref="bool"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">An optional format string. Ignored for <see cref="bool"/>.</param>
    public void AppendFormatted(bool value, int alignment = 0, string? format = null) =>
        _writer.AppendFormatted(value, alignment, format);

    /// <summary>Appends a formatted <see cref="byte"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom numeric format string, or <c>null</c> for default.</param>
    public void AppendFormatted(byte value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="sbyte"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom numeric format string, or <c>null</c> for default.</param>
    public void AppendFormatted(sbyte value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="short"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom numeric format string, or <c>null</c> for default.</param>
    public void AppendFormatted(short value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="ushort"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom numeric format string, or <c>null</c> for default.</param>
    public void AppendFormatted(ushort value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="int"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom numeric format string, or <c>null</c> for default.</param>
    public void AppendFormatted(int value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="uint"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom numeric format string, or <c>null</c> for default.</param>
    public void AppendFormatted(uint value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="long"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom numeric format string, or <c>null</c> for default.</param>
    public void AppendFormatted(long value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="ulong"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom numeric format string, or <c>null</c> for default.</param>
    public void AppendFormatted(ulong value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="float"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom numeric format string, or <c>null</c> for default.</param>
    public void AppendFormatted(float value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="double"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom numeric format string, or <c>null</c> for default.</param>
    public void AppendFormatted(double value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="decimal"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom numeric format string, or <c>null</c> for default.</param>
    public void AppendFormatted(decimal value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="char"/> value.</summary>
    /// <param name="value">The character to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">An optional format string, or <c>null</c> for default.</param>
    public void AppendFormatted(char value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="Guid"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard GUID format string (e.g., <c>"D"</c>, <c>"N"</c>), or <c>null</c> for default.</param>
    public void AppendFormatted(Guid value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="DateTime"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom date and time format string, or <c>null</c> for default.</param>
    public void AppendFormatted(DateTime value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="DateTimeOffset"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom date and time format string, or <c>null</c> for default.</param>
    public void AppendFormatted(DateTimeOffset value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted <see cref="TimeSpan"/> value.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A standard or custom TimeSpan format string, or <c>null</c> for default.</param>
    public void AppendFormatted(TimeSpan value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    /// <summary>Appends a formatted string value.</summary>
    /// <param name="value">The string to append, or <c>null</c> to append padding only if <paramref name="alignment"/> is non-zero.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">An optional format string. Ignored for <see cref="string"/>.</param>
    public void AppendFormatted(string? value, int alignment = 0, string? format = null)
    {
        if (value is null)
        {
            if (alignment != 0)
            {
                _writer.AppendWhitespace(Math.Abs(alignment));
            }
            return;
        }

        _writer.AppendFormatted(value, alignment, format);
    }

    /// <summary>Appends a formatted value of any type that supports composite formatting.</summary>
    /// <typeparam name="T">The type of the value to format.</typeparam>
    /// <param name="value">The value to format. Uses <see cref="IUtf8SpanFormattable"/> when available, otherwise falls back to composite formatting.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A format string whose interpretation depends on <typeparamref name="T"/>, or <c>null</c> for default.</param>
    public void AppendFormatted<T>(T value, int alignment = 0, string? format = null) =>
        _writer.AppendFormatted(value, alignment, format);

    /// <summary>Appends a formatted nullable value that implements <see cref="IUtf8SpanFormattable"/>.</summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="value">The nullable value to format. If <c>null</c>, appends padding only when <paramref name="alignment"/> is non-zero.</param>
    /// <param name="alignment">Minimum field width. Positive values right-align, negative values left-align.</param>
    /// <param name="format">A format string passed to <see cref="IUtf8SpanFormattable.TryFormat"/>, or <c>null</c> for default.</param>
    public void AppendFormatted<T>(T? value, int alignment = 0, string? format = null)
        where T : struct, IUtf8SpanFormattable
    {
        if (value.HasValue)
        {
            AppendUtf8Formattable(value.GetValueOrDefault(), alignment, format);
        }
        else if (alignment != 0)
        {
            _writer.AppendWhitespace(Math.Abs(alignment));
        }
    }

    /// <summary>Appends a span of characters directly as UTF-8.</summary>
    /// <param name="value">The character span to encode and append.</param>
    public void AppendFormatted(ReadOnlySpan<char> value) => _writer.AppendFormatted(value);

    /// <summary>Appends a span of UTF-8 bytes directly without re-encoding.</summary>
    /// <param name="value">The UTF-8 bytes to append. Must be valid UTF-8.</param>
    public void AppendFormatted(ReadOnlySpan<byte> value) => _writer.AppendFormatted(value);

    private void AppendUtf8Formattable<T>(T value, int alignment, string? format)
        where T : IUtf8SpanFormattable
    {
        _writer.Flush();
        var start = _arena->Utf8Length;
        var sizeHint = 64;
        int bytesWritten;

        while (true)
        {
            var destination = ArenaWriter.GetWritableUtf8Span(_arena, sizeHint);
            if (value.TryFormat(destination, out bytesWritten, format, null))
            {
                break;
            }

            sizeHint = checked(destination.Length + 1);
        }

        ArenaWriter.AdvanceUtf8(_arena, bytesWritten);

        if (alignment > bytesWritten)
        {
            var padding = alignment - bytesWritten;
            _ = ArenaWriter.GetWritableUtf8Span(_arena, padding);
            new Span<byte>(_arena->Utf8 + start, bytesWritten).CopyTo(
                new Span<byte>(_arena->Utf8 + start + padding, bytesWritten)
            );
            new Span<byte>(_arena->Utf8 + start, padding).Fill((byte)' ');
            ArenaWriter.AdvanceUtf8(_arena, padding);
        }
        else if (alignment < 0 && -alignment > bytesWritten)
        {
            var padding = -alignment - bytesWritten;
            ArenaWriter.GetWritableUtf8Span(_arena, padding)[..padding].Fill((byte)' ');
            ArenaWriter.AdvanceUtf8(_arena, padding);
        }

        _writer = new Utf8StringWriter<ArenaUtf8BufferWriter>(new ArenaUtf8BufferWriter(_arena));
    }

    internal void Complete(out RenderArena* arena, out uint offset, out uint length)
    {
        _writer.Flush();
        arena = _arena;
        offset = _offset;
        length = checked((uint)_arena->Utf8Length - _offset);
    }
}

internal readonly unsafe struct ArenaUtf8BufferWriter(RenderArena* arena) : IBufferWriter<byte>
{
    public void Advance(int count) => ArenaWriter.AdvanceUtf8(arena, count);

    public Memory<byte> GetMemory(int sizeHint = 0) =>
        throw new NotSupportedException(
            "The render arena exposes unmanaged Span<byte> storage only."
        );

    public Span<byte> GetSpan(int sizeHint = 0) => ArenaWriter.GetWritableUtf8Span(arena, sizeHint);
}
