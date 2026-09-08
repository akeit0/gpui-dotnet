<!-- Generated from bindings/schema.json. Do not edit. -->

# Semantic IDs

The schema owns these IDs. See [Binding generation](BINDING_GENERATION.md) for allocation rules and [ABI](ABI.md) for payload contracts.

## Components

| ID | Component |
| --- | --- |
| 1 | `Div` |
| 2 | `Text` |
| 3 | `Spacer` |
| 4 | `Divider` |
| 5 | `Badge` |
| 6 | `Button` |
| 7 | `Checkbox` |
| 8 | `Radio` |
| 9 | `Scroll` |
| 10 | `List` |
| 11 | `Table` |
| 12 | `Input` |
| 13 | `Slider` |
| 14 | `Image` |
| 15 | `Drawing` |
| 16 | `Path` |
| 17 | `Dynamic` |
| 18 | `Overlay` |
| 19 | `Tooltip` |
| 20 | `ContextMenu` |
| 21 | `PopoverMenu` |
| 22 | `DockArea` |
| 23 | `DockSplit` |
| 24 | `DockTabs` |
| 25 | `DockPanel` |
| 26 | `DockRegion` |
| 27 | `NativeExtension` |

## Operations

| Group | Allocated range |
| --- | --- |
| [identity](#identity) | 1–99 |
| [flex](#flex) | 100–199 |
| [geometry](#geometry) | 200–299 |
| [spacing](#spacing) | 300–399 |
| [grid](#grid) | 400–499 |
| [paint](#paint) | 500–599 |
| [typography](#typography) | 600–699 |
| [interaction](#interaction) | 700–799 |
| [accessibility](#accessibility) | 800–899 |
| [focus](#focus) | 900–999 |
| [scroll](#scroll) | 1000–1099 |
| [list](#list) | 1100–1199 |
| [table](#table) | 1200–1299 |
| [input](#input) | 1300–1399 |
| [slider](#slider) | 1400–1499 |
| [overlay](#overlay) | 1500–1599 |
| [tooltip](#tooltip) | 1600–1699 |
| [menus](#menus) | 1700–1799 |
| [image](#image) | 1800–1899 |
| [drawing](#drawing) | 1900–1999 |
| [dock](#dock) | 2000–2099 |
| [dynamic](#dynamic) | 2100–2199 |

### identity

| ID | Operation | Capability |
| --- | --- | --- |
| 1 | `ElementOwner` | `interactive` |
| 2 | `ResourceOwner` | `native_state` |

### flex

| ID | Operation | Capability |
| --- | --- | --- |
| 100 | `AlignContent` | `styled` |
| 101 | `Hidden` | `styled` |
| 102 | `HStack` | `layout` |
| 103 | `BasisPercent` | `styled` |
| 104 | `BasisPx` | `styled` |
| 105 | `Grow` | `styled` |
| 106 | `Shrink` | `styled` |
| 107 | `Wrap` | `styled` |
| 108 | `ItemsBaseline` | `styled` |
| 109 | `ItemsCenter` | `styled` |
| 110 | `ItemsEnd` | `styled` |
| 111 | `ItemsStart` | `styled` |
| 112 | `ItemsStretch` | `styled` |
| 113 | `JustifyBetween` | `styled` |
| 114 | `JustifyCenter` | `styled` |
| 115 | `JustifyEnd` | `styled` |
| 116 | `JustifyStart` | `styled` |
| 117 | `SelfBaseline` | `styled` |
| 118 | `SelfCenter` | `styled` |
| 119 | `SelfEnd` | `styled` |
| 120 | `SelfFlexEnd` | `styled` |
| 121 | `SelfFlexStart` | `styled` |
| 122 | `SelfStart` | `styled` |
| 123 | `SelfStretch` | `styled` |
| 124 | `VStack` | `layout` |

### geometry

| ID | Operation | Capability |
| --- | --- | --- |
| 200 | `Absolute` | `styled` |
| 201 | `AspectRatio` | `styled` |
| 202 | `BottomPercent` | `styled` |
| 203 | `Bottom` | `styled` |
| 204 | `Height` | `styled` |
| 205 | `Height` | `styled` |
| 206 | `InsetPercent` | `styled` |
| 207 | `Inset` | `styled` |
| 208 | `LeftPercent` | `styled` |
| 209 | `Left` | `styled` |
| 210 | `MaxHeightPercent` | `styled` |
| 211 | `MaxHeight` | `styled` |
| 212 | `MaxWidthPercent` | `styled` |
| 213 | `MaxWidth` | `styled` |
| 214 | `MinHeightPercent` | `styled` |
| 215 | `MinHeight` | `styled` |
| 216 | `MinWidthPercent` | `styled` |
| 217 | `MinWidth` | `styled` |
| 218 | `Relative` | `styled` |
| 219 | `RightPercent` | `styled` |
| 220 | `Right` | `styled` |
| 221 | `TopPercent` | `styled` |
| 222 | `Top` | `styled` |
| 223 | `Width` | `styled` |
| 224 | `Width` | `styled` |

### spacing

| ID | Operation | Capability |
| --- | --- | --- |
| 300 | `GapPercent` | `styled` |
| 301 | `Gap` | `styled` |
| 302 | `GapXPercent` | `styled` |
| 303 | `GapX` | `styled` |
| 304 | `GapYPercent` | `styled` |
| 305 | `GapY` | `styled` |
| 306 | `MarginBottomPercent` | `styled` |
| 307 | `MarginBottom` | `styled` |
| 308 | `MarginLeftPercent` | `styled` |
| 309 | `MarginLeft` | `styled` |
| 310 | `MarginPercent` | `styled` |
| 311 | `Margin` | `styled` |
| 312 | `MarginRightPercent` | `styled` |
| 313 | `MarginRight` | `styled` |
| 314 | `MarginTopPercent` | `styled` |
| 315 | `MarginTop` | `styled` |
| 316 | `MarginXPercent` | `styled` |
| 317 | `MarginX` | `styled` |
| 318 | `MarginYPercent` | `styled` |
| 319 | `MarginY` | `styled` |
| 320 | `PaddingBottomPercent` | `styled` |
| 321 | `PaddingBottom` | `styled` |
| 322 | `PaddingLeftPercent` | `styled` |
| 323 | `PaddingLeft` | `styled` |
| 324 | `PaddingPercent` | `styled` |
| 325 | `Padding` | `styled` |
| 326 | `PaddingRightPercent` | `styled` |
| 327 | `PaddingRight` | `styled` |
| 328 | `PaddingTopPercent` | `styled` |
| 329 | `PaddingTop` | `styled` |
| 330 | `PaddingXPercent` | `styled` |
| 331 | `PaddingX` | `styled` |
| 332 | `PaddingYPercent` | `styled` |
| 333 | `PaddingY` | `styled` |

### grid

| ID | Operation | Capability |
| --- | --- | --- |
| 400 | `ColEnd` | `styled` |
| 401 | `ColEndAuto` | `styled` |
| 402 | `ColSpan` | `styled` |
| 403 | `ColSpanFull` | `styled` |
| 404 | `ColStart` | `styled` |
| 405 | `ColStartAuto` | `styled` |
| 406 | `Grid` | `styled` |
| 407 | `GridCols` | `styled` |
| 408 | `GridColsMaxContent` | `styled` |
| 409 | `GridColsMinContent` | `styled` |
| 410 | `GridRows` | `styled` |
| 411 | `GridRowsMaxContent` | `styled` |
| 412 | `GridRowsMinContent` | `styled` |
| 413 | `RowEnd` | `styled` |
| 414 | `RowEndAuto` | `styled` |
| 415 | `RowSpan` | `styled` |
| 416 | `RowSpanFull` | `styled` |
| 417 | `RowStart` | `styled` |
| 418 | `RowStartAuto` | `styled` |

### paint

| ID | Operation | Capability |
| --- | --- | --- |
| 500 | `Background` | `styled` |
| 501 | `BorderColor` | `styled` |
| 502 | `BorderStyle` | `styled` |
| 503 | `BorderWidth` | `styled` |
| 504 | `Opacity` | `styled` |
| 505 | `OverflowHidden` | `styled` |
| 506 | `OverflowXHidden` | `styled` |
| 507 | `OverflowYHidden` | `styled` |
| 508 | `RadiusBottomLeftPx` | `styled` |
| 509 | `RadiusBottomRightPx` | `styled` |
| 510 | `Radius` | `styled` |
| 511 | `RadiusTopLeftPx` | `styled` |
| 512 | `RadiusTopRightPx` | `styled` |
| 513 | `ShadowBlur` | `styled` |
| 514 | `ShadowColor` | `styled` |
| 515 | `ShadowOffset` | `styled` |
| 516 | `ShadowSpread` | `styled` |
| 517 | `Visibility` | `styled` |

### typography

| ID | Operation | Capability |
| --- | --- | --- |
| 600 | `FontFallbacks` | `styled` |
| 601 | `FontFamily` | `styled` |
| 602 | `FontFeatures` | `styled` |
| 603 | `FontSize` | `styled` |
| 604 | `FontStyle` | `styled` |
| 605 | `FontWeight` | `styled` |
| 606 | `LineClamp` | `styled` |
| 607 | `LineHeightPercent` | `styled` |
| 608 | `LineHeightPx` | `styled` |
| 609 | `LineThrough` | `styled` |
| 610 | `TextAlign` | `styled` |
| 611 | `TextBackground` | `styled` |
| 612 | `TextDecorationColor` | `styled` |
| 613 | `TextDecorationNone` | `styled` |
| 614 | `TextDecorationSolid` | `styled` |
| 615 | `TextDecorationWavy` | `styled` |
| 616 | `TextEllipsis` | `styled` |
| 617 | `TextColor` | `styled` |
| 618 | `TextTruncate` | `styled` |
| 619 | `Underline` | `styled` |
| 620 | `WhiteSpace` | `styled` |

### interaction

| ID | Operation | Capability |
| --- | --- | --- |
| 700 | `ActiveBackground` | `interactive` |
| 701 | `ActiveBorderColor` | `interactive` |
| 702 | `ActiveTextColor` | `interactive` |
| 703 | `Checked` | `checkable` |
| 704 | `Cursor` | `styled` |
| 705 | `Disabled` | `disableable` |
| 706 | `HoverBackground` | `interactive` |
| 707 | `HoverBorderColor` | `interactive` |
| 708 | `HoverTextColor` | `interactive` |
| 709 | `IsolateShortcuts` | `shortcut_scope` |
| 710 | `OnClick` | `interactive` |
| 711 | `OnFileDrop` | `key_mouse` |
| 712 | `OnHover` | `key_mouse` |
| 713 | `OnKeyDown` | `key_mouse` |
| 714 | `OnKeyUp` | `key_mouse` |
| 715 | `OnModifiersChanged` | `key_mouse` |
| 716 | `OnMouseDown` | `key_mouse` |
| 717 | `OnMouseDownOut` | `key_mouse` |
| 718 | `OnMouseMove` | `key_mouse` |
| 719 | `OnMouseUp` | `key_mouse` |
| 720 | `OnMouseUpOut` | `key_mouse` |
| 721 | `OnScrollWheel` | `key_mouse` |
| 722 | `OnShortcut` | `shortcut_scope` |
| 723 | `WindowControlArea` | `window_control` |

### accessibility

| ID | Operation | Capability |
| --- | --- | --- |
| 800 | `AccessibleDescription` | `accessible` |
| 801 | `AccessibleName` | `accessible` |

### focus

| ID | Operation | Capability |
| --- | --- | --- |
| 900 | `FocusTabStop` | `focus_target` |
| 901 | `FocusTarget` | `focus_target` |

### scroll

| ID | Operation | Capability |
| --- | --- | --- |
| 1000 | `ScrollAxis` | `scrollable` |
| 1001 | `ScrollbarGutter` | `native_state` |
| 1002 | `ScrollbarWidth` | `native_state` |
| 1003 | `ShowScrollbar` | `native_state` |
| 1004 | `SmoothScroll` | `native_state` |

### list

| ID | Operation | Capability |
| --- | --- | --- |
| 1100 | `ListAlignment` | `virtualized` |
| 1101 | `ListBatchSize` | `virtualized` |
| 1102 | `ListContentRevision` | `virtualized` |
| 1103 | `ListEstimatedItemExtentPx` | `virtualized` |
| 1104 | `ListItemCount` | `virtualized` |
| 1105 | `ListItemId` | `styled` |
| 1106 | `OnActivated` | `virtualized` |
| 1107 | `OnContextMenuRequested` | `virtualized` |
| 1108 | `OnSelectionRequested` | `virtualized` |
| 1109 | `OnTooltipRequested` | `virtualized` |
| 1110 | `ListOverdrawPx` | `virtualized` |
| 1111 | `ListProjectionRevision` | `virtualized` |
| 1112 | `ListRenderer` | `virtualized` |
| 1113 | `ItemTooltipTarget` | `styled` |
| 1114 | `ListOrientation` | `virtualized` |

### table

| ID | Operation | Capability |
| --- | --- | --- |
| 1200 | `TableCellColumn` | `styled` |
| 1201 | `TableColumn` | `table` |
| 1202 | `TableHeaderBackgroundRgba` | `table` |
| 1203 | `TableHeaderBorderRgba` | `table` |
| 1204 | `TableHeaderTextRgba` | `table` |
| 1205 | `TableShowHeader` | `table` |

### input

| ID | Operation | Capability |
| --- | --- | --- |
| 1300 | `InputCaretRgba` | `input` |
| 1301 | `InputDisabled` | `input` |
| 1302 | `OnChanged` | `input` |
| 1303 | `OnFocusChanged` | `input` |
| 1304 | `OnSubmitted` | `input` |
| 1305 | `InputOnWriteCompleted` | `input` |
| 1306 | `InputPassword` | `input` |
| 1307 | `InputPlaceholderRgba` | `input` |
| 1308 | `InputReadOnly` | `input` |
| 1309 | `InputSelectionRgba` | `input` |

### slider

| ID | Operation | Capability |
| --- | --- | --- |
| 1400 | `SliderAxis` | `slider` |
| 1401 | `SliderDisabled` | `slider` |
| 1402 | `SliderFillRgba` | `slider` |
| 1403 | `SliderMax` | `slider` |
| 1404 | `SliderMin` | `slider` |
| 1405 | `SliderOnChanged` | `slider` |
| 1406 | `SliderOnReleased` | `slider` |
| 1407 | `SliderRangeEnd` | `slider` |
| 1408 | `SliderRangeStart` | `slider` |
| 1409 | `SliderScale` | `slider` |
| 1410 | `SliderStep` | `slider` |
| 1411 | `SliderThumbBorderRgba` | `slider` |
| 1412 | `SliderThumbRgba` | `slider` |
| 1413 | `SliderTrackRgba` | `slider` |
| 1414 | `SliderValue` | `slider` |

### overlay

| ID | Operation | Capability |
| --- | --- | --- |
| 1500 | `OverlayBackdropRgba` | `overlay` |
| 1501 | `OverlayDismissOnBackdrop` | `overlay` |
| 1502 | `OverlayDismissOnEscape` | `overlay` |
| 1503 | `OverlayMarginPx` | `overlay` |
| 1504 | `OverlayModal` | `overlay` |
| 1505 | `OnDismiss` | `overlay` |
| 1506 | `OverlayPlacement` | `overlay` |
| 1507 | `OverlayPriority` | `overlay` |

### tooltip

| ID | Operation | Capability |
| --- | --- | --- |
| 1600 | `TooltipAlignment` | `tooltip_options` |
| 1601 | `TooltipGapPx` | `tooltip_options` |
| 1602 | `TooltipHideDelayMs` | `tooltip_options` |
| 1603 | `TooltipMarginPx` | `tooltip_options` |
| 1604 | `TooltipPlacement` | `tooltip_options` |
| 1605 | `TooltipItemAnchor` | `tooltip` |
| 1606 | `TooltipShowDelayMs` | `tooltip_options` |

### menus

| ID | Operation | Capability |
| --- | --- | --- |
| 1700 | `ContextMenuMarginPx` | `context_menu` |
| 1701 | `ContextMenuPriority` | `context_menu` |
| 1702 | `ContextMenuItemAnchor` | `context_menu` |
| 1703 | `PopoverMenuMarginPx` | `popover_menu` |
| 1704 | `PopoverMenuPriority` | `popover_menu` |

### image

| ID | Operation | Capability |
| --- | --- | --- |
| 1800 | `Grayscale` | `image` |
| 1801 | `Fit` | `image` |

### drawing

| ID | Operation | Capability |
| --- | --- | --- |
| 1900 | `DrawingViewBoxOrigin` | `drawing` |
| 1901 | `DrawingViewBoxSize` | `drawing` |
| 1902 | `PathArcFlags` | `path` |
| 1903 | `PathArcRadii` | `path` |
| 1904 | `PathArcRotation` | `path` |
| 1905 | `PathArcTo` | `path` |
| 1906 | `PathCircleCenter` | `path` |
| 1907 | `PathCircleRadius` | `path` |
| 1908 | `PathClose` | `path` |
| 1909 | `PathCubicControlA` | `path` |
| 1910 | `PathCubicControlB` | `path` |
| 1911 | `PathCubicTo` | `path` |
| 1912 | `PathDashPx` | `path` |
| 1913 | `PathFillRgba` | `path` |
| 1914 | `PathFillRule` | `path` |
| 1915 | `PathLineTo` | `path` |
| 1916 | `PathMoveTo` | `path` |
| 1917 | `PathQuadraticControl` | `path` |
| 1918 | `PathQuadraticTo` | `path` |
| 1919 | `PathStrokeRgba` | `path` |
| 1920 | `PathStrokeWidthPx` | `path` |

### dock

| ID | Operation | Capability |
| --- | --- | --- |
| 2000 | `DockActiveIndex` | `dock_tabs` |
| 2001 | `DockAxis` | `dock_split` |
| 2002 | `DockInitialSizePx` | `dock_container` |
| 2003 | `DockLocked` | `dock_area` |
| 2004 | `DockOnClosed` | `dock_area` |
| 2005 | `DockOnLayout` | `dock_area` |
| 2006 | `DockPanelClosable` | `dock_panel` |
| 2007 | `DockPanelInnerPadding` | `dock_panel` |
| 2008 | `DockPanelZoomable` | `dock_panel` |
| 2009 | `DockRegionCollapsible` | `dock_region` |
| 2010 | `DockRegionOpen` | `dock_region` |
| 2011 | `DockRegionSide` | `dock_region` |

### dynamic

| ID | Operation | Capability |
| --- | --- | --- |
| 2100 | `DynamicActive` | `dynamic` |

## Resource commands

| Resource ID | Resource | Command ID | Command |
| --- | --- | --- | --- |
| 1 | `Scroll` | 100 | `ScrollToOffset` |
| 1 | `Scroll` | 101 | `ScrollToTop` |
| 1 | `Scroll` | 102 | `ScrollToBottom` |
| 1 | `Scroll` | 103 | `ScrollToLeft` |
| 1 | `Scroll` | 104 | `ScrollToRight` |
| 2 | `List` | 200 | `ListScrollToItem` |
| 2 | `List` | 201 | `ListSplice` |
| 2 | `List` | 202 | `ListReset` |
| 2 | `List` | 203 | `ListRefresh` |
| 3 | `Input` | 300 | `InputFocus` |
| 3 | `Input` | 301 | `InputBlur` |
| 3 | `Input` | 302 | `InputSetValue` |
| 3 | `Input` | 303 | `InputSelectAll` |
| 3 | `Input` | 304 | `InputSetValueIfCurrent` |
| 3 | `Input` | 305 | `InputSetValueIfCurrentWithResult` |
| 4 | `Slider` | 400 | `SliderSetValue` |
| 5 | `Dock` | 500 | `DockClosePanel` |
| 5 | `Dock` | 501 | `DockSetRegionOpen` |
| 5 | `Dock` | 502 | `DockImportLayout` |
| 5 | `Dock` | 503 | `DockExportLayout` |
| 6 | `Focus` | 600 | `FocusTargetFocus` |
| 6 | `Focus` | 601 | `FocusTargetBlur` |

## Control events

| ID | Family | Event |
| --- | --- | --- |
| 100 | `input` | `Changed` |
| 101 | `input` | `FocusChanged` |
| 102 | `input` | `Submitted` |
| 200 | `input_write` | `Completed` |
| 300 | `list` | `Activated` |
| 301 | `list` | `ContextMenuRequested` |
| 302 | `list` | `SelectionRequested` |
| 303 | `list` | `TooltipRequested` |
| 400 | `slider` | `Changed` |
| 401 | `slider` | `Released` |
| 500 | `dock` | `LayoutChanged` |
| 501 | `dock` | `LayoutExported` |
| 502 | `dock` | `PanelClosed` |
| 600 | `key` | `Down` |
| 601 | `key` | `Up` |
| 700 | `mouse` | `Down` |
| 701 | `mouse` | `DownOut` |
| 702 | `mouse` | `Move` |
| 703 | `mouse` | `Up` |
| 704 | `mouse` | `UpOut` |
| 800 | `modifiers` | `Changed` |
| 900 | `hover` | `Changed` |
| 1000 | `scroll` | `Wheel` |
| 1100 | `file` | `Dropped` |
| 1200 | `shortcut` | `Invoked` |
