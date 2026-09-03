# Optional editor extension

The rich text editor is an optional component family, separate from the default GPUI.NET managed
and native hosts. This document defines its ownership and protocol direction. The existing
single-line Input keeps its independent contract.

## Current slice

`Gpui.Editor` is a schema-only managed assembly. It provides the typed `ui.Editor` builder and
references `GPUI.NET.Core`; it carries no native asset. `gpui-dotnet-editor-host` is a custom native
host that links the base runtime with a retained `gpui-component` Editor provider.

The current probe supports a one-shot UTF-8 document bootstrap, a language identifier,
disabled/read-only state, line numbers, an optional fixed line-number column width, folding,
whitespace visibility, and opt-in typed callbacks. The optional native host currently bundles the
Rust Tree-sitter grammar; other language identifiers degrade to plain text. Without a fixed width,
the native editor sizes the number column from the document line count. Rust retains the Rope,
incremental parse state, selection, undo history, scrolling, focus, and IME state. Each native or
managed editing transaction increments the document revision and reports one minimal contiguous
UTF-8 replacement. The controller proves focus, revision-checked selection, whole-document
replacement, and one contiguous edit; stale revisions and invalid UTF-8 byte ranges produce an
explicit rejection event.

The extension ID, protocol version, component kinds, flags, and schema hash come from
`src/Gpui.Editor/schema.json`. The normal binding generator emits matching C# and Rust constants,
so neither side hand-maintains protocol numbers.

## Native startup

The shared runtime initializes `gpui-base` only, so the editor provider owns its component
foundation through the extension lifecycle seam: at startup the runtime publishes the resolved
managed roles as the `ResolvedTheme` global, then runs each installed provider's `initialize`
(the editor installs `gpui-component` globals, including the `Theme` its `Editor` reads) followed
by `apply_theme` (which projects the resolved roles into the component theme). Every managed
theme update replaces the global and re-runs `apply_theme`. The default host installs no
providers, so neither hook runs there and no component globals exist in that binary.

## Ownership

The native document is authoritative between explicit application commands. Pointer and keyboard
editing, selection, composition, scrolling, syntax work, and undo grouping do not require managed
renders. C# owns application-level document identity, persistence, dirty/saved state, and decisions
about replacing or reconciling a document.

The editor will not be a continuously controlled text component. Feeding a complete string through
each render would copy the Rope across the managed boundary and reset or conflict with native
interaction state.

## Protocol channels

The complete contract uses four channels with different costs:

| Channel | Purpose | Frequency |
|---|---|---|
| Snapshot | Cheap presentation and policy such as read-only, gutters, and language | Dirty managed renders |
| Bootstrap | One owned UTF-8 document payload, buffered until the keyed resource exists | Once per opened document |
| Commands | Focus plus revision-checked selection, edit, and replacement operations | Application initiated |
| Events | Revisioned edit batches and coarse focus/selection/document status | Opt-in and transaction based |

Bootstrap, commands, and events must use a generic extension-neutral transport in Core. Numeric
command and event IDs, flags, and payload formats belong to the independently hashed editor schema.
The base runtime validates routing and memory safety; the linked provider validates editor-specific
payloads. Adding another extension must not add editor branches to Core.

Render remains pure. In particular, opening a document or issuing a command cannot occur inside
`Render()`. `EditorController` uses the stable mounted View command route and may enqueue its
bootstrap from `OnMounted` before the corresponding extension node is first materialized. Native
pending-command storage owns copied bytes and discards them when the owner View unmounts or a
committed snapshot omits the resource identity.

## Revision and edit model

Each retained document has a monotonically increasing `u64` revision. A native editing transaction
emits one change event containing:

- the base and resulting revisions;
- an origin identifying native input or a managed command;
- non-overlapping edits expressed in UTF-8 byte offsets against the base revision;
- deleted byte lengths and inserted UTF-8 bytes.

When a schema emits multiple edits, they are ordered from the end of the document toward the start,
so consumers can apply them without rebasing later offsets. The current editor provider emits one
minimal contiguous edit per native transaction. Managed callback code copies the borrowed packet
before returning through FFI; typed event values expose slices of that owned UTF-8 payload without
eagerly allocating a complete UTF-16 document.

Document-position and mutation commands carry an expected revision. A stale command cannot
silently target newer native input: the provider rejects it and reports the current revision so the
application can rebase or replace deliberately. Invalid UTF-8 byte ranges are rejected the same
way. Focus does not require a revision. Programmatic mutations advance the same revision sequence
and identify their command origin, allowing C# to acknowledge them without feedback loops.

Selection changes are not part of every document delta. They are separately opt-in and may be
coalesced to one notification per frame; the native editor remains authoritative for caret motion
between those notifications.

## Lifecycle and identity

Identity is the normal extension resource tuple: session, owner View, extension ID, component kind,
key, version, and schema hash. Removing the declaration drops the native editor and its history.
Reusing the key within the same owner retains the document; changing extension version or hash
creates a different resource identity.

Event tokens are render-bound declarations and disappear when no longer declared. Controllers do
not retain UI ownership, and commands racing owner teardown follow the normal closed-session
behavior.

Initial document content is not part of the render configuration. `EditorController.Bootstrap`
transfers it once through the command channel, so unrelated renders never resend a large document.
