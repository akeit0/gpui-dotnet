use gpui::{Div, ParentElement, Styled, div, px, rgba};

use crate::{
    semantic::{NativeAdapter, OP_CHECKED},
    snapshot::{SnapshotNode, ValidatedSnapshot},
};

/// Applies the semantic defaults for one Rust-side component adapter. Adding a component is a
/// schema change plus one adapter branch here; managed code never mirrors GPUI's Rust trait API.
pub(crate) fn apply_defaults(
    adapter: NativeAdapter,
    element: Div,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
) -> Div {
    match adapter {
        NativeAdapter::Button => element
            .rounded(px(6.))
            .border(px(1.))
            .border_color(rgba(0xCBD5E1FF)),
        NativeAdapter::Checkbox => checkbox(element, has_u32_flag(node, snapshot, OP_CHECKED)),
        NativeAdapter::Radio => radio(element, has_u32_flag(node, snapshot, OP_CHECKED)),
        NativeAdapter::Badge => element
            .flex()
            .items_center()
            .rounded_full()
            .p(px(5.))
            .bg(rgba(0xDBEAFEFF))
            .text_color(rgba(0x1D4ED8FF))
            .text_xs(),
        NativeAdapter::Divider => element.h(px(1.)).w_full().bg(rgba(0xE2E8F0FF)),
        NativeAdapter::Spacer => element.flex_grow(),
        NativeAdapter::Div
        | NativeAdapter::Text
        | NativeAdapter::Scroll
        | NativeAdapter::List
        | NativeAdapter::Image
        | NativeAdapter::Input
        | NativeAdapter::Overlay
        | NativeAdapter::Tooltip
        | NativeAdapter::ContextMenu => element,
    }
}

fn checkbox(element: Div, checked: bool) -> Div {
    let mut indicator = div()
        .flex()
        .items_center()
        .justify_center()
        .w(px(16.))
        .h(px(16.))
        .rounded(px(4.))
        .border(px(1.))
        .border_color(rgba(0x94A3B8FF));
    if checked {
        indicator = indicator
            .bg(rgba(0x2563EBFF))
            .text_color(rgba(0xFFFFFFFF))
            .text_xs()
            .child("✓");
    } else {
        indicator = indicator.bg(rgba(0xFFFFFFFF));
    }

    element
        .flex()
        .flex_row()
        .items_center()
        .gap(px(8.))
        .child(indicator)
}

fn radio(element: Div, checked: bool) -> Div {
    let mut indicator = div()
        .flex()
        .items_center()
        .justify_center()
        .w(px(16.))
        .h(px(16.))
        .rounded_full()
        .border(px(1.))
        .border_color(rgba(0x94A3B8FF))
        .bg(rgba(0xFFFFFFFF));
    if checked {
        indicator = indicator.child(
            div()
                .w(px(8.))
                .h(px(8.))
                .rounded_full()
                .bg(rgba(0x2563EBFF)),
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
