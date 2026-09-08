use super::*;

pub(super) fn apply_styles<T: Styled>(
    mut element: T,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
) -> T {
    for op in snapshot.ops(node) {
        let value = f32::from_bits(op.a as u32);
        element = match op.code {
            OP_FLEX => element.flex().flex_row(),
            OP_V_STACK => element.flex().flex_col(),
            OP_ITEMS_CENTER => element.items_center(),
            OP_JUSTIFY_CENTER => element.justify_center(),
            OP_JUSTIFY_BETWEEN => element.justify_between(),
            OP_FLEX_GROW => element.flex_grow(value),
            OP_ASPECT_RATIO => element.aspect_ratio(value),
            OP_GAP_PX => element.gap(px(value)),
            OP_PADDING_PX => element.p(px(value)),
            OP_MARGIN_PX => element.m(px(value)),
            OP_MARGIN_X_PX => element.mx(px(value)),
            OP_MARGIN_Y_PX => element.my(px(value)),
            OP_MARGIN_TOP_PX => element.mt(px(value)),
            OP_MARGIN_BOTTOM_PX => element.mb(px(value)),
            OP_MARGIN_LEFT_PX => element.ml(px(value)),
            OP_MARGIN_RIGHT_PX => element.mr(px(value)),
            OP_MARGIN_PERCENT => element.m(relative(value / 100.0)),
            OP_MARGIN_X_PERCENT => element.mx(relative(value / 100.0)),
            OP_MARGIN_Y_PERCENT => element.my(relative(value / 100.0)),
            OP_MARGIN_TOP_PERCENT => element.mt(relative(value / 100.0)),
            OP_MARGIN_BOTTOM_PERCENT => element.mb(relative(value / 100.0)),
            OP_MARGIN_LEFT_PERCENT => element.ml(relative(value / 100.0)),
            OP_MARGIN_RIGHT_PERCENT => element.mr(relative(value / 100.0)),
            OP_PADDING_X_PX => element.px(px(value)),
            OP_PADDING_Y_PX => element.py(px(value)),
            OP_PADDING_TOP_PX => element.pt(px(value)),
            OP_PADDING_BOTTOM_PX => element.pb(px(value)),
            OP_PADDING_LEFT_PX => element.pl(px(value)),
            OP_PADDING_RIGHT_PX => element.pr(px(value)),
            OP_PADDING_PERCENT => element.p(DefiniteLength::Fraction(value / 100.0)),
            OP_PADDING_X_PERCENT => element.px(DefiniteLength::Fraction(value / 100.0)),
            OP_PADDING_Y_PERCENT => element.py(DefiniteLength::Fraction(value / 100.0)),
            OP_PADDING_TOP_PERCENT => element.pt(DefiniteLength::Fraction(value / 100.0)),
            OP_PADDING_BOTTOM_PERCENT => element.pb(DefiniteLength::Fraction(value / 100.0)),
            OP_PADDING_LEFT_PERCENT => element.pl(DefiniteLength::Fraction(value / 100.0)),
            OP_PADDING_RIGHT_PERCENT => element.pr(DefiniteLength::Fraction(value / 100.0)),
            OP_GAP_X_PX => element.gap_x(px(value)),
            OP_GAP_Y_PX => element.gap_y(px(value)),
            OP_GAP_PERCENT => element.gap(DefiniteLength::Fraction(value / 100.0)),
            OP_GAP_X_PERCENT => element.gap_x(DefiniteLength::Fraction(value / 100.0)),
            OP_GAP_Y_PERCENT => element.gap_y(DefiniteLength::Fraction(value / 100.0)),
            OP_MIN_WIDTH_PX => element.min_w(px(value)),
            OP_MIN_HEIGHT_PX => element.min_h(px(value)),
            OP_MAX_WIDTH_PX => element.max_w(px(value)),
            OP_MAX_HEIGHT_PX => element.max_h(px(value)),
            OP_MIN_WIDTH_PERCENT => element.min_w(relative(value / 100.0)),
            OP_MIN_HEIGHT_PERCENT => element.min_h(relative(value / 100.0)),
            OP_MAX_WIDTH_PERCENT => element.max_w(relative(value / 100.0)),
            OP_MAX_HEIGHT_PERCENT => element.max_h(relative(value / 100.0)),
            OP_FLEX_BASIS_PX => element.flex_basis(px(value)),
            OP_FLEX_BASIS_PERCENT => element.flex_basis(relative(value / 100.0)),
            OP_FLEX_SHRINK => element.flex_shrink(value),
            OP_FLEX_WRAP => match op.a as u32 {
                0 => element.flex_nowrap(),
                1 => element.flex_wrap(),
                2 => element.flex_wrap_reverse(),
                _ => element,
            },
            OP_ITEMS_START => element.items_start(),
            OP_ITEMS_END => element.items_end(),
            OP_ITEMS_BASELINE => element.items_baseline(),
            OP_ITEMS_STRETCH => element.items_stretch(),
            OP_JUSTIFY_START => element.justify_start(),
            OP_JUSTIFY_END => element.justify_end(),
            OP_DISPLAY_GRID => element.grid(),
            OP_GRID_COLS => element.grid_cols(op.a as u16),
            OP_GRID_COLS_MIN_CONTENT => element.grid_cols_min_content(op.a as u16),
            OP_GRID_COLS_MAX_CONTENT => element.grid_cols_max_content(op.a as u16),
            OP_GRID_ROWS => element.grid_rows(op.a as u16),
            OP_GRID_ROWS_MIN_CONTENT => element.grid_rows_min_content(op.a as u16),
            OP_GRID_ROWS_MAX_CONTENT => element.grid_rows_max_content(op.a as u16),
            OP_COL_SPAN => element.col_span(op.a as u16),
            OP_ROW_SPAN => element.row_span(op.a as u16),
            OP_COL_START => element.col_start(op.a as u16 as i16),
            OP_COL_END => element.col_end(op.a as u16 as i16),
            OP_ROW_START => element.row_start(op.a as u16 as i16),
            OP_ROW_END => element.row_end(op.a as u16 as i16),
            OP_COL_START_AUTO => element.col_start_auto(),
            OP_COL_END_AUTO => element.col_end_auto(),
            OP_ROW_START_AUTO => element.row_start_auto(),
            OP_ROW_END_AUTO => element.row_end_auto(),
            OP_COL_SPAN_FULL => element.col_span_full(),
            OP_ROW_SPAN_FULL => element.row_span_full(),
            OP_DISPLAY_NONE => element.hidden(),
            OP_ALIGN_CONTENT => match op.a as u32 {
                0 => element.content_normal(),
                1 => element.content_start(),
                2 => element.content_end(),
                3 => element.content_center(),
                4 => element.content_stretch(),
                5 => element.content_between(),
                6 => element.content_evenly(),
                7 => element.content_around(),
                _ => element,
            },
            OP_SELF_START => element.self_start(),
            OP_SELF_END => element.self_end(),
            OP_SELF_FLEX_START => element.self_flex_start(),
            OP_SELF_FLEX_END => element.self_flex_end(),
            OP_SELF_CENTER => element.self_center(),
            OP_SELF_BASELINE => element.self_baseline(),
            OP_SELF_STRETCH => element.self_stretch(),
            OP_RELATIVE => element.relative(),
            OP_ABSOLUTE => element.absolute(),
            OP_TOP_PX => element.top(px(value)),
            OP_LEFT_PX => element.left(px(value)),
            OP_RIGHT_PX => element.right(px(value)),
            OP_BOTTOM_PX => element.bottom(px(value)),
            OP_INSET_PX => element.inset(px(value)),
            OP_TOP_PERCENT => {
                element.top(Length::Definite(DefiniteLength::Fraction(value / 100.0)))
            }
            OP_LEFT_PERCENT => {
                element.left(Length::Definite(DefiniteLength::Fraction(value / 100.0)))
            }
            OP_RIGHT_PERCENT => {
                element.right(Length::Definite(DefiniteLength::Fraction(value / 100.0)))
            }
            OP_BOTTOM_PERCENT => {
                element.bottom(Length::Definite(DefiniteLength::Fraction(value / 100.0)))
            }
            OP_INSET_PERCENT => {
                element.inset(Length::Definite(DefiniteLength::Fraction(value / 100.0)))
            }
            OP_OVERFLOW_HIDDEN => element.overflow_hidden(),
            OP_OVERFLOW_X_HIDDEN => element.overflow_x_hidden(),
            OP_OVERFLOW_Y_HIDDEN => element.overflow_y_hidden(),
            OP_OPACITY => element.opacity(value),
            OP_TEXT_ALIGN => match op.a as u32 {
                0 => element.text_align(TextAlign::Left),
                1 => element.text_align(TextAlign::Center),
                2 => element.text_align(TextAlign::Right),
                _ => element,
            },
            OP_WHITE_SPACE => match op.a as u32 {
                0 => element.whitespace_normal(),
                1 => element.whitespace_nowrap(),
                _ => element,
            },
            OP_VISIBILITY => match op.a as u32 {
                0 => element.visible(),
                1 => element.invisible(),
                _ => element,
            },
            OP_LINE_CLAMP => element.line_clamp(op.a as usize),
            OP_WIDTH_PX => element.w(px(value)),
            OP_WIDTH_PERCENT => element.w(relative(value / 100.0)),
            OP_BACKGROUND_RGBA => element.bg(rgba(op.a as u32)),
            OP_HEIGHT_PX => element.h(px(value)),
            OP_HEIGHT_PERCENT => element.h(relative(value / 100.0)),
            OP_BORDER_RGBA => element.border_color(rgba(op.a as u32)),
            OP_BORDER_WIDTH_PX => element.border(px(value)),
            OP_BORDER_STYLE => match op.a as u32 {
                // Solid is the GPUI default, so it applies no change.
                1 => element.border_dashed(),
                _ => element,
            },
            OP_RADIUS_PX => element.rounded(px(value)),
            OP_RADIUS_TOP_LEFT_PX => element.rounded_tl(px(value)),
            OP_RADIUS_TOP_RIGHT_PX => element.rounded_tr(px(value)),
            OP_RADIUS_BOTTOM_LEFT_PX => element.rounded_bl(px(value)),
            OP_RADIUS_BOTTOM_RIGHT_PX => element.rounded_br(px(value)),
            OP_TEXT_RGBA => element.text_color(rgba(op.a as u32)),
            OP_TEXT_BACKGROUND => element.text_bg(rgba(op.a as u32)),
            OP_FONT_SIZE_PX => element.text_size(px(value)),
            OP_FONT_WEIGHT => element.font_weight(FontWeight::from(value)),
            OP_FONT_FAMILY => match snapshot.last_data_op(node, OP_FONT_FAMILY) {
                Some(family) => element.font_family(family),
                None => element,
            },
            OP_FONT_FEATURES => match snapshot
                .last_data_op(node, OP_FONT_FEATURES)
                .as_deref()
                .and_then(parse_font_features)
            {
                Some(features) => element.font_features(features),
                None => element,
            },
            OP_FONT_FALLBACKS => match snapshot
                .last_data_op(node, OP_FONT_FALLBACKS)
                .as_deref()
                .and_then(parse_font_fallbacks)
            {
                Some(fallbacks) => element.font(apply_font_fallbacks(snapshot, node, fallbacks)),
                None => element,
            },
            OP_FONT_STYLE => match op.a as u32 {
                // GPUI exposes no oblique setter, so the schema enum covers normal and italic only.
                1 => element.italic(),
                _ => element.not_italic(),
            },
            OP_TEXT_ELLIPSIS => element.text_ellipsis(),
            OP_LINE_HEIGHT_PX => element.line_height(px(value)),
            OP_LINE_HEIGHT_PERCENT => element.line_height(DefiniteLength::Fraction(value / 100.0)),
            OP_UNDERLINE => element.underline(),
            OP_LINE_THROUGH => element.line_through(),
            OP_TEXT_DECORATION_NONE => element.text_decoration_none(),
            OP_TEXT_DECORATION_COLOR => {
                element.text_decoration_color(Hsla::from(rgba(op.a as u32)))
            }
            OP_TEXT_DECORATION_WAVY => element.text_decoration_wavy(),
            OP_TEXT_DECORATION_SOLID => element.text_decoration_solid(),
            OP_TEXT_TRUNCATE => match snapshot.last_data_op(node, OP_TEXT_TRUNCATE) {
                Some(truncation) => element.text_overflow(TextOverflow::Truncate(truncation)),
                None => element,
            },
            OP_SHADOW_COLOR | OP_SHADOW_OFFSET | OP_SHADOW_BLUR | OP_SHADOW_SPREAD => {
                apply_box_shadow(element, node, snapshot)
            }
            OP_CURSOR => match op.a as u32 {
                0 => element.cursor(CursorStyle::Arrow),
                1 => element.cursor(CursorStyle::IBeam),
                2 => element.cursor(CursorStyle::Crosshair),
                3 => element.cursor(CursorStyle::ClosedHand),
                4 => element.cursor(CursorStyle::OpenHand),
                5 => element.cursor(CursorStyle::PointingHand),
                6 => element.cursor(CursorStyle::ResizeLeft),
                7 => element.cursor(CursorStyle::ResizeRight),
                8 => element.cursor(CursorStyle::ResizeLeftRight),
                9 => element.cursor(CursorStyle::ResizeUp),
                10 => element.cursor(CursorStyle::ResizeDown),
                11 => element.cursor(CursorStyle::ResizeUpDown),
                12 => element.cursor(CursorStyle::ResizeUpLeftDownRight),
                13 => element.cursor(CursorStyle::ResizeUpRightDownLeft),
                14 => element.cursor(CursorStyle::ResizeColumn),
                15 => element.cursor(CursorStyle::ResizeRow),
                16 => element.cursor(CursorStyle::IBeamCursorForVerticalLayout),
                17 => element.cursor(CursorStyle::OperationNotAllowed),
                18 => element.cursor(CursorStyle::DragLink),
                19 => element.cursor(CursorStyle::DragCopy),
                20 => element.cursor(CursorStyle::ContextualMenu),
                _ => element,
            },
            OP_HOVER_BACKGROUND_RGBA
            | OP_HOVER_TEXT_RGBA
            | OP_HOVER_BORDER_RGBA
            | OP_ACTIVE_BACKGROUND_RGBA
            | OP_ACTIVE_TEXT_RGBA
            | OP_ACTIVE_BORDER_RGBA => element,
            OP_CHECKED
            | OP_DISABLED
            | OP_ELEMENT_OWNER
            | OP_ON_CLICK
            | OP_ON_FILE_DROP
            | OP_ON_HOVER
            | OP_ON_KEY_DOWN
            | OP_ON_KEY_UP
            | OP_ON_MODIFIERS_CHANGED
            | OP_ON_MOUSE_DOWN
            | OP_ON_MOUSE_DOWN_OUT
            | OP_ON_MOUSE_MOVE
            | OP_ON_MOUSE_UP
            | OP_ON_MOUSE_UP_OUT
            | OP_ON_SCROLL_WHEEL
            | OP_RESOURCE_OWNER
            | OP_SCROLL_AXIS
            | OP_SMOOTH_SCROLL
            | OP_SHOW_SCROLLBAR
            | OP_LIST_ITEM_COUNT
            | OP_LIST_RENDERER
            | OP_LIST_BATCH_SIZE
            | OP_LIST_OVERDRAW_PX
            | OP_LIST_ALIGNMENT
            | OP_LIST_ESTIMATED_ITEM_HEIGHT_PX
            | OP_IMAGE_OBJECT_FIT
            | OP_IMAGE_GRAYSCALE
            | OP_OVERLAY_PLACEMENT
            | OP_OVERLAY_PRIORITY
            | OP_OVERLAY_MARGIN_PX
            | OP_OVERLAY_MODAL
            | OP_OVERLAY_BACKDROP_RGBA
            | OP_OVERLAY_DISMISS_ON_BACKDROP
            | OP_OVERLAY_DISMISS_ON_ESCAPE
            | OP_OVERLAY_ON_DISMISS => element,
            _ => element,
        };
    }
    element
}

/// Builds the full font description for a fallbacks declaration. Sibling font operations
/// supply the remaining parts so per-field last-wins ordering holds; anything unset falls back
/// to the GPUI font defaults.
pub(super) fn apply_font_fallbacks(
    snapshot: &ValidatedSnapshot,
    node: &SnapshotNode,
    fallbacks: FontFallbacks,
) -> Font {
    let mut font = gpui::font(
        snapshot
            .last_data_op(node, OP_FONT_FAMILY)
            .unwrap_or_else(|| ".SystemUIFont".into()),
    );
    if let Some(operation) = last_op(snapshot, node, OP_FONT_WEIGHT) {
        font.weight = FontWeight::from(f32::from_bits(operation.a as u32));
    }
    if last_op(snapshot, node, OP_FONT_STYLE).is_some_and(|operation| operation.a == 1) {
        font.style = FontStyle::Italic;
    }
    if let Some(features) = snapshot
        .last_data_op(node, OP_FONT_FEATURES)
        .as_deref()
        .and_then(parse_font_features)
    {
        font.features = features;
    }
    font.fallbacks = Some(fallbacks);
    font
}

/// Composes the single-layer box shadow from its scalar parts. Any shadow operation on the
/// node enables the layer; parts without an explicit operation fall back to transparent black,
/// zero offset, and zero blur/spread. Multi-layer shadow vectors are not exposed.
pub(super) fn apply_box_shadow<T: Styled>(
    element: T,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
) -> T {
    let color = last_op(snapshot, node, OP_SHADOW_COLOR).map_or(0, |op| op.a as u32);
    let (x, y) = last_op(snapshot, node, OP_SHADOW_OFFSET).map_or((0.0, 0.0), op_f32x2);
    let blur =
        last_op(snapshot, node, OP_SHADOW_BLUR).map_or(0.0, |op| f32::from_bits(op.a as u32));
    let spread =
        last_op(snapshot, node, OP_SHADOW_SPREAD).map_or(0.0, |op| f32::from_bits(op.a as u32));
    element.shadow(vec![BoxShadow {
        color: Hsla::from(rgba(color)),
        offset: point(px(x), px(y)),
        blur_radius: px(blur),
        spread_radius: px(spread),
        inset: false,
    }])
}

#[derive(Clone, Copy, Default)]
pub(super) struct InteractionPaint {
    pub(super) background: Option<u32>,
    pub(super) text: Option<u32>,
    pub(super) border: Option<u32>,
}

impl InteractionPaint {
    fn is_empty(self) -> bool {
        self.background.is_none() && self.text.is_none() && self.border.is_none()
    }
}

pub(super) fn interaction_paint(
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
    background_code: u16,
    text_code: u16,
    border_code: u16,
) -> InteractionPaint {
    InteractionPaint {
        background: last_op(snapshot, node, background_code).map(|op| op.a as u32),
        text: last_op(snapshot, node, text_code).map(|op| op.a as u32),
        border: last_op(snapshot, node, border_code).map(|op| op.a as u32),
    }
}

pub(super) fn apply_paint<T: Styled>(mut style: T, paint: InteractionPaint) -> T {
    if let Some(color) = paint.background {
        style = style.bg(rgba(color));
    }
    if let Some(color) = paint.text {
        style = style.text_color(rgba(color));
    }
    if let Some(color) = paint.border {
        style = style.border_color(rgba(color));
    }
    style
}

/// Resolves only transient interaction states in native GPUI. Application variants and durable
/// states such as selection remain managed concerns and are already flattened into base ops.
pub(super) fn apply_interaction_styles<T>(
    mut element: T,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
    theme: NativeTheme,
) -> T
where
    T: StatefulInteractiveElement + Styled,
{
    element = presentation::focus_ring(element, theme.border_focused);

    let hover = interaction_paint(
        node,
        snapshot,
        OP_HOVER_BACKGROUND_RGBA,
        OP_HOVER_TEXT_RGBA,
        OP_HOVER_BORDER_RGBA,
    );
    let has_hover_paint = !hover.is_empty();
    element = if !has_hover_paint {
        element.hover(move |style| style.bg(rgba(theme.element_hover)))
    } else {
        element.hover(move |style| apply_paint(style, hover))
    };

    let active = interaction_paint(
        node,
        snapshot,
        OP_ACTIVE_BACKGROUND_RGBA,
        OP_ACTIVE_TEXT_RGBA,
        OP_ACTIVE_BORDER_RGBA,
    );
    element = if active.is_empty() && has_hover_paint {
        // Preserve an explicit hover palette (notably destructive caption controls) when the
        // app has not supplied a distinct pressed palette.
        presentation::pressed_feedback(element)
    } else if active.is_empty() {
        element.active(move |style| style.bg(rgba(theme.element_active)))
    } else {
        element.active(move |style| apply_paint(style, active))
    };

    element
}
