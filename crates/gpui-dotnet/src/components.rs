use gpui::{Div, ParentElement, Styled, div, px, rgba};

use crate::{
    semantic::{NativeAdapter, OP_CHECKED},
    snapshot::{SnapshotNode, ValidatedSnapshot},
    theme::NativeTheme,
};

/// Applies the semantic defaults for one Rust-side component adapter. Adding a component is a
/// schema change plus one adapter branch here; managed code never mirrors GPUI's Rust trait API.
pub(crate) fn apply_defaults(
    adapter: NativeAdapter,
    element: Div,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
    theme: NativeTheme,
) -> Div {
    match adapter {
        NativeAdapter::Button => element
            .rounded(px(6.))
            .border(px(1.))
            .border_color(rgba(theme.border))
            .bg(rgba(theme.element_background))
            .text_color(rgba(theme.text)),
        NativeAdapter::Checkbox => {
            checkbox(element, has_u32_flag(node, snapshot, OP_CHECKED), theme)
        }
        NativeAdapter::Radio => radio(element, has_u32_flag(node, snapshot, OP_CHECKED), theme),
        NativeAdapter::Badge => element
            .flex()
            .items_center()
            .rounded_full()
            .p(px(5.))
            .bg(rgba(theme.info_background))
            .text_color(rgba(theme.info))
            .text_xs(),
        NativeAdapter::Divider => element.h(px(1.)).w_full().bg(rgba(theme.border_variant)),
        NativeAdapter::Spacer => element.flex_grow(1.0),
        NativeAdapter::Div
        | NativeAdapter::Text
        | NativeAdapter::Scroll
        | NativeAdapter::List
        | NativeAdapter::Table
        | NativeAdapter::Image
        | NativeAdapter::Input
        | NativeAdapter::Slider
        | NativeAdapter::Overlay
        | NativeAdapter::Tooltip
        | NativeAdapter::ContextMenu
        | NativeAdapter::PopoverMenu
        | NativeAdapter::Drawing
        | NativeAdapter::Dynamic
        | NativeAdapter::Path => element,
    }
}

fn checkbox(element: Div, checked: bool, theme: NativeTheme) -> Div {
    let mut indicator = div()
        .flex()
        .items_center()
        .justify_center()
        .w(px(16.))
        .h(px(16.))
        .rounded(px(4.))
        .border(px(1.))
        .border_color(rgba(theme.border));
    if checked {
        indicator = indicator
            .bg(rgba(theme.accent))
            .text_color(rgba(theme.text_on_accent))
            .text_xs()
            .child("✓");
    } else {
        indicator = indicator.bg(rgba(theme.element_background));
    }

    element
        .flex()
        .flex_row()
        .items_center()
        .gap(px(8.))
        .child(indicator)
}

fn radio(element: Div, checked: bool, theme: NativeTheme) -> Div {
    let mut indicator = div()
        .flex()
        .items_center()
        .justify_center()
        .w(px(16.))
        .h(px(16.))
        .rounded_full()
        .border(px(1.))
        .border_color(rgba(theme.border))
        .bg(rgba(theme.element_background));
    if checked {
        indicator = indicator.child(
            div()
                .w(px(8.))
                .h(px(8.))
                .rounded_full()
                .bg(rgba(theme.accent)),
        );
    }

    element
        .flex()
        .flex_row()
        .items_center()
        .gap(px(8.))
        .child(indicator)
}

fn has_u32_flag(node: &SnapshotNode, snapshot: &ValidatedSnapshot, code: u16) -> bool {
    snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|operation| operation.code == code)
        .is_some_and(|operation| operation.a != 0)
}
