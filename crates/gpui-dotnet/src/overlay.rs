use std::{
    cell::{Cell, RefCell},
    rc::Rc,
};

use crate::resources::ResourceKey;

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub(crate) enum OverlayKind {
    Overlay,
    ContextMenu,
    PopoverMenu,
    Tooltip,
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub(crate) struct OverlayToken {
    key: ResourceKey,
    kind: OverlayKind,
    sequence: u64,
}

struct OverlayEntry {
    token: OverlayToken,
    priority: u32,
    captures_input: bool,
}

/// The dismissal stack for one native window. Entries are rebuilt from the current managed
/// snapshot, so a layer that is no longer rendered cannot retain stale dismissal authority.
/// Priority is the primary ordering rule; declaration order breaks ties deterministically.
#[derive(Default)]
pub(crate) struct OverlayStack {
    entries: RefCell<Vec<OverlayEntry>>,
    next_sequence: Cell<u64>,
}

impl OverlayStack {
    pub(crate) fn new() -> Rc<Self> {
        Rc::new(Self::default())
    }

    pub(crate) fn begin_frame(&self) {
        self.entries.borrow_mut().clear();
        self.next_sequence.set(0);
    }

    pub(crate) fn register(
        &self,
        key: ResourceKey,
        kind: OverlayKind,
        priority: u32,
        captures_input: bool,
    ) -> OverlayToken {
        let sequence = self.next_sequence.get().wrapping_add(1).max(1);
        self.next_sequence.set(sequence);
        let token = OverlayToken {
            key,
            kind,
            sequence,
        };
        self.entries.borrow_mut().push(OverlayEntry {
            token: token.clone(),
            priority,
            captures_input,
        });
        token
    }

    pub(crate) fn set_captures_input(&self, token: &OverlayToken, captures_input: bool) {
        if let Some(entry) = self
            .entries
            .borrow_mut()
            .iter_mut()
            .find(|entry| &entry.token == token)
        {
            entry.captures_input = captures_input;
        }
    }

    pub(crate) fn is_topmost(&self, token: &OverlayToken) -> bool {
        self.entries
            .borrow()
            .iter()
            .filter(|entry| entry.captures_input)
            .max_by_key(|entry| (entry.priority, entry.token.sequence))
            .is_some_and(|entry| &entry.token == token)
    }
}

#[cfg(test)]
mod tests {
    use std::sync::Arc;

    use gpui::SharedString;

    use super::*;

    fn key(value: &'static str) -> ResourceKey {
        ResourceKey::new(7, SharedString::new(Arc::<str>::from(value)))
    }

    #[test]
    fn priority_wins_and_declaration_order_breaks_ties() {
        let stack = OverlayStack::default();
        stack.begin_frame();
        let first = stack.register(key("first"), OverlayKind::Overlay, 10, true);
        let second = stack.register(key("second"), OverlayKind::Overlay, 10, true);
        let high = stack.register(key("high"), OverlayKind::Overlay, 20, true);

        assert!(!stack.is_topmost(&first));
        assert!(!stack.is_topmost(&second));
        assert!(stack.is_topmost(&high));

        stack.begin_frame();
        let first = stack.register(key("first"), OverlayKind::Overlay, 10, true);
        let second = stack.register(key("second"), OverlayKind::Overlay, 10, true);
        assert!(!stack.is_topmost(&first));
        assert!(stack.is_topmost(&second));
    }

    #[test]
    fn non_capturing_layers_do_not_shadow_dismissal() {
        let stack = OverlayStack::default();
        stack.begin_frame();
        let modal = stack.register(key("modal"), OverlayKind::Overlay, 10, true);
        let tooltip = stack.register(key("tooltip"), OverlayKind::Tooltip, 200, false);

        assert!(stack.is_topmost(&modal));
        assert!(!stack.is_topmost(&tooltip));
    }

    #[test]
    fn capture_activation_controls_dismissal_authority() {
        let stack = OverlayStack::default();
        stack.begin_frame();
        let hidden = stack.register(key("hidden"), OverlayKind::ContextMenu, 300, false);
        let visible = stack.register(key("visible"), OverlayKind::ContextMenu, 300, false);

        assert!(!stack.is_topmost(&hidden));
        assert!(!stack.is_topmost(&visible));

        stack.set_captures_input(&hidden, true);
        assert!(stack.is_topmost(&hidden));

        stack.set_captures_input(&visible, true);
        assert!(!stack.is_topmost(&hidden));
        assert!(stack.is_topmost(&visible));
    }

    #[test]
    fn begin_frame_removes_stale_tokens() {
        let stack = OverlayStack::default();
        let modal = stack.register(key("modal"), OverlayKind::Overlay, 10, true);
        assert!(stack.is_topmost(&modal));

        stack.begin_frame();
        assert!(!stack.is_topmost(&modal));
    }
}
