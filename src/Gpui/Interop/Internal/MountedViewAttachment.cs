using System.Collections.Concurrent;

namespace Gpui.Interop.Internal;

// UI-thread storage; pooled independently of the non-reusable View identity and command route.
internal sealed class MountedViewAttachment
{
    private static readonly ConcurrentBag<MountedViewAttachment> UiAttachmentPool = [];
    private static int _uiAttachmentPoolCount;
    private const int MaxPooledUiAttachments = 256;

    internal ViewEventRegistry Events { get; } = new();
    internal uint ViewHandle { get; private set; }
    internal int ManagedThreadId { get; private set; }
    internal ulong NextResourceKeyId { get; set; }

    internal void Activate(ViewBase owner, uint viewHandle)
    {
        ViewHandle = viewHandle;
        ManagedThreadId = Environment.CurrentManagedThreadId;
        Events.Activate(owner, viewHandle);
    }

    internal void Reset()
    {
        AssertAccess();
        Events.Reset();
        ViewHandle = 0;
        NextResourceKeyId = 0;
        ManagedThreadId = 0;
    }

    internal void AssertAccess()
    {
        if (ManagedThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException(
                "Managed View render and event state is confined to the GPUI application thread."
            );
    }

    internal static MountedViewAttachment Rent(ViewBase owner, uint viewHandle)
    {
        if (UiAttachmentPool.TryTake(out var attachment))
        {
            Interlocked.Decrement(ref _uiAttachmentPoolCount);
        }
        else
        {
            attachment = new MountedViewAttachment();
        }

        attachment.Activate(owner, viewHandle);
        return attachment;
    }

    internal static void Return(MountedViewAttachment attachment)
    {
        attachment.Reset();
        if (Interlocked.Increment(ref _uiAttachmentPoolCount) <= MaxPooledUiAttachments)
        {
            UiAttachmentPool.Add(attachment);
            return;
        }

        Interlocked.Decrement(ref _uiAttachmentPoolCount);
    }
}
