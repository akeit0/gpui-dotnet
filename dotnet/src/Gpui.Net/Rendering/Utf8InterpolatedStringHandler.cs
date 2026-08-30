using System.Buffers;
using System.Runtime.CompilerServices;
using Gpui.Interop;
using Utf8StringInterpolation;

namespace Gpui;

/// <summary>
/// Compiler-facing interpolated string handler that formats values directly into a render arena.
/// Application code normally creates this implicitly through <c>ui.Text($"...")</c>.
/// </summary>
[InterpolatedStringHandler]
public unsafe ref struct Utf8InterpolatedStringHandler
{
    private readonly RenderArena* _arena;
    private readonly uint _offset;
    private Utf8StringWriter<ArenaUtf8BufferWriter> _writer;

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

    public void AppendLiteral(string value) => _writer.AppendLiteral(value);

    public void AppendFormatted(bool value, int alignment = 0, string? format = null) =>
        _writer.AppendFormatted(value, alignment, format);

    public void AppendFormatted(byte value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(sbyte value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(short value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(ushort value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(int value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(uint value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(long value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(ulong value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(float value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(double value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(decimal value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(char value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(Guid value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(DateTime value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(DateTimeOffset value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

    public void AppendFormatted(TimeSpan value, int alignment = 0, string? format = null) =>
        AppendUtf8Formattable(value, alignment, format);

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

    public void AppendFormatted<T>(T value, int alignment = 0, string? format = null) =>
        _writer.AppendFormatted(value, alignment, format);

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

    public void AppendFormatted(ReadOnlySpan<char> value) => _writer.AppendFormatted(value);

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
