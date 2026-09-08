namespace Gpui.Interop.Internal;

internal enum NativeStatusDomain
{
    Snapshot,
    ResourceCommand,
    ExtensionCommand,
    ExtensionSupport,
    Notification,
    ArtifactInvalidation,
    ApplicationCommand,
    ApplicationMenu,
}

// Status numbers are local to an entry point. Keep unknown and ambiguous values intact;
// never infer a resource-command error from a snapshot's overlapping numeric status.
internal static class NativeStatus
{
    internal static string Describe(NativeStatusDomain domain, int status)
    {
        var reason = status switch
        {
            0 => "Success",
            -99 => "NativePanic: the host caught a Rust panic",
            _ => Reason(domain, status) ?? "UnknownStatus: no symbolic diagnostic is available",
        };
        return $"{domain}.{reason} (status {status})";
    }

    private static string? Reason(NativeStatusDomain domain, int status)
    {
        if (domain is NativeStatusDomain.ResourceCommand or NativeStatusDomain.ExtensionCommand
            or NativeStatusDomain.Notification or NativeStatusDomain.ArtifactInvalidation)
        {
            var delivery = status switch
            {
                -30 => "SessionMissing: the native session is no longer registered",
                -31 => "SessionClosed: the native session queue is closed",
                -32 => "SessionLockFailed: native session bookkeeping is unavailable",
                -33 => "SessionQueueFull: the native session queue rejected the command",
                -34 when domain is NativeStatusDomain.ResourceCommand or NativeStatusDomain.ExtensionCommand =>
                    "ResourceNotDeclared: the resource is not declared in the accepted snapshot",
                _ => null,
            };
            if (delivery is not null) return delivery;
        }
        if (domain is NativeStatusDomain.ApplicationCommand or NativeStatusDomain.ApplicationMenu)
        {
            var delivery = status switch
            {
                -40 => "ApplicationMissing",
                -41 => "ApplicationQueueClosed",
                -42 => "ApplicationLockFailed",
                -43 => "ApplicationQueueFull",
                _ => null,
            };
            if (delivery is not null) return delivery;
        }
        return domain switch
        {
            NativeStatusDomain.ResourceCommand => status switch
            {
                -50 => "InvalidCommandPointer",
                -51 => "InvalidCommandEnvelope: check owner, key, lengths, and reserved fields",
                -52 => "InvalidKeyUtf8",
                -53 => "UnsupportedResourceCommand",
                -54 => "InvalidCommandPayload",
                -55 => "InvalidCommandText",
                _ => null,
            },
            NativeStatusDomain.ExtensionCommand or NativeStatusDomain.ExtensionSupport => status switch
            {
                -80 => "InvalidExtensionIdentity",
                -81 => "ExtensionNotInstalled",
                -82 => "ExtensionSchemaMismatch",
                -83 when domain == NativeStatusDomain.ExtensionCommand => "InvalidExtensionCommandEnvelope",
                -84 when domain == NativeStatusDomain.ExtensionCommand => "InvalidExtensionCommandIdentity",
                -85 when domain == NativeStatusDomain.ExtensionCommand => "ExtensionCommandRejected",
                _ => null,
            },
            NativeStatusDomain.ArtifactInvalidation => status switch
            {
                -1 => "InvalidArtifactBatch",
                -2 => "InvalidArtifactIdentity",
                _ => null,
            },
            NativeStatusDomain.ApplicationCommand => status switch
            {
                -60 => "InvalidCommandPointer",
                -61 => "InvalidApplicationCommandEnvelope",
                -62 => "InvalidApplicationCommandPayload",
                -63 => "InvalidTitleUtf8",
                -64 => "InvalidThemePayload",
                _ => null,
            },
            NativeStatusDomain.ApplicationMenu => status switch
            {
                -64 => "InvalidMenuPointer",
                -65 => "InvalidMenuEnvelope",
                -66 => "InvalidMenuRecord",
                -67 => "InvalidMenuTitleUtf8",
                -68 => "InvalidMenuHierarchy",
                -69 => "InvalidMenuActionIdentity",
                _ => null,
            },
            NativeStatusDomain.Snapshot => status switch
            {
                -1 => "InvalidArenaPointer",
                -2 => "InvalidArenaLengths",
                -3 => "InvalidRootIndex",
                -4 => "InvalidArenaBuffers",
                -5 => "InvalidDataRange",
                -6 => "InvalidOperationNode",
                -7 => "InvalidChildIndex",
                -8 => "InvalidTextPayload",
                -9 => "UnknownComponent",
                -10 => "MultipleParents",
                -11 => "RootHasParent",
                -12 => "CyclicGraph",
                -13 => "UnreachableNode",
                -14 => "InvalidComponentData",
                -15 => "UnknownOperation",
                -16 => "InvalidOperationValueKind",
                -17 => "OperationNotApplicable",
                -18 => "NonFiniteValue",
                -19 => "MissingCallbackToken",
                -20 => "ChildrenNotAllowed",
                -21 => "InvalidBoolean",
                -22 => "InvalidNodeFlagsOrFontStyle",
                -23 => "UnexpectedOperationPayload",
                -24 => "NonCanonicalOperationValue",
                -25 => "MissingOwnerIdentity",
                -26 => "InvalidScrollAxis",
                -27 => "InvalidBatchSize",
                -28 => "InvalidListAlignment",
                -29 => "InvalidListOrScrollbarDimensions",
                -30 => "InvalidImageObjectFit",
                -31 => "InvalidInputPayload",
                -32 => "InvalidOverlayChildren",
                -33 => "InvalidOverlayPlacement",
                -34 => "InvalidOverlayMargin",
                -35 => "InvalidTooltipPlacement",
                -36 => "InvalidTooltipAlignment",
                -37 => "InvalidTooltipSpacing",
                -38 => "InvalidTooltipChildren",
                -39 => "InvalidWindowControlArea",
                -40 => "InvalidPublicationOrMenuMargin",
                -41 => "InvalidContextMenuChildren",
                -42 => "InvalidPopoverMenuChildren",
                -43 => "InvalidSliderDeclaration",
                -44 => "InvalidFlexShrink",
                -45 => "InvalidFlexWrap",
                -46 => "InvalidFlexGrow",
                -47 => "InvalidOpacity",
                -48 => "InvalidTextAlignment",
                -49 => "InvalidLineClamp",
                -50 => "InvalidCursor",
                -51 => "InvalidAspectRatio",
                -52 => "InvalidWhiteSpace",
                -53 => "InvalidVisibility",
                -54 => "InvalidAlignContent",
                -55 => "InvalidFontWeight",
                -57 => "InvalidTableColumn",
                -58 => "InvalidDrawingStructure",
                -59 => "InvalidDrawingGeometry",
                -60 => "InvalidDockValueOrDynamicChildren",
                -61 => "InvalidDockStructure",
                -62 => "InvalidExtensionPayload",
                // These codes have multiple meanings even inside the render path.
                -56 => "InvalidResourceIdentityOrBorderStyle",
                -63 => "EmptyOperationDataOrWrongRowCount",
                -64 => "InvalidFontDataOrMissingArtifact",
                -66 => "InvalidShortcutBinding",
                _ => null,
            },
            _ => null,
        };
    }
}
