use super::*;
use gpui_base::{Button, Checkbox, Radio};

/// Shared presentation only. Callers supply path-specific identity, materialize children in
/// their own context, and attach callbacks after composition so row and View lifetimes stay separate.
pub(super) struct ControlPresentation<'a> {
    node: &'a SnapshotNode,
    snapshot: &'a ValidatedSnapshot,
    theme: NativeTheme,
    disabled: bool,
}

impl<'a> ControlPresentation<'a> {
    pub(super) fn new(
        node: &'a SnapshotNode,
        snapshot: &'a ValidatedSnapshot,
        theme: NativeTheme,
    ) -> Self {
        Self {
            node,
            snapshot,
            theme,
            disabled: components::has_u32_flag(node, snapshot, OP_DISABLED),
        }
    }

    pub(super) fn button(
        &self,
        id: ElementId,
        children: impl IntoIterator<Item = AnyElement>,
    ) -> Button {
        let element = self.compose(components::button(id, self.disabled, self.theme), children);
        apply_window_control_area(element, self.node, self.snapshot)
    }

    pub(super) fn checkbox(
        &self,
        id: ElementId,
        children: impl IntoIterator<Item = AnyElement>,
    ) -> Checkbox {
        let checked = components::has_u32_flag(self.node, self.snapshot, OP_CHECKED);
        self.compose(
            components::checkbox(id, checked, self.disabled, self.theme),
            children,
        )
    }

    pub(super) fn radio(
        &self,
        id: ElementId,
        children: impl IntoIterator<Item = AnyElement>,
    ) -> Radio {
        let checked = components::has_u32_flag(self.node, self.snapshot, OP_CHECKED);
        self.compose(
            components::radio(id, checked, self.disabled, self.theme),
            children,
        )
    }

    fn compose<T>(&self, mut element: T, children: impl IntoIterator<Item = AnyElement>) -> T
    where
        T: StatefulInteractiveElement + Styled + ParentElement,
    {
        element = crate::accessibility::Accessibility::from_snapshot(self.node, self.snapshot)
            .apply(element);
        if let Some(label) = accessibility_label(self.node, self.snapshot) {
            element = element.aria_label(label);
        }
        element = element.children(children);
        element = apply_styles(element, self.node, self.snapshot);
        if !self.disabled {
            if use_default_cursor(self.node, self.snapshot) {
                element = element.cursor_pointer();
            }
            element = apply_interaction_styles(element, self.node, self.snapshot, self.theme);
        }
        presentation::disabled(element, self.disabled)
    }
}
