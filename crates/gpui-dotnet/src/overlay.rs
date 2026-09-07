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
        // Registrations captured by deferred callbacks must not alias a later frame.
    }

    pub(crate) fn register(
        &self,
        key: ResourceKey,
        kind: OverlayKind,
        priority: u32,
        captures_input: bool,
    ) -> OverlayToken {
        let sequence = self
            .next_sequence
            .get()
            .checked_add(1)
            .expect("overlay registration sequence exhausted");
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

    #[test]
    fn identical_layers_in_a_new_frame_reject_old_dismissal_and_capture_tokens() {
        let stack = OverlayStack::default();
        for kind in [
            OverlayKind::Overlay,
            OverlayKind::ContextMenu,
            OverlayKind::PopoverMenu,
            OverlayKind::Tooltip,
        ] {
            let old = stack.register(key("layer"), kind, 10, true);
            stack.begin_frame();
            let current = stack.register(key("layer"), kind, 10, true);
            assert_ne!(old, current);
            assert!(!stack.is_topmost(&old));
            stack.set_captures_input(&old, false);
            assert!(stack.is_topmost(&current));
            stack.set_captures_input(&current, false);
            stack.set_captures_input(&old, true);
            assert!(!stack.is_topmost(&current));
            stack.begin_frame();
        }
    }

    #[gpui::test]
    fn deferred_focus_from_a_previous_frame_cannot_steal_current_focus(
        cx: &mut gpui::TestAppContext,
    ) {
        let (_, window_cx) = cx.add_window_view(|_, _| gpui::Empty);
        let stack = OverlayStack::new();
        let stale_ran = Rc::new(Cell::new(false));
        let current_ran = Rc::new(Cell::new(false));
        window_cx.update(|window, cx| {
            let focus = cx.focus_handle();
            let old = stack.register(key("modal"), OverlayKind::Overlay, 10, true);
            let old_stack = stack.clone();
            let stale_ran = stale_ran.clone();
            window.defer(cx, move |window, cx| {
                if old_stack.is_topmost(&old) {
                    stale_ran.set(true);
                    focus.focus(window, cx);
                }
            });
            stack.begin_frame();
            let current = stack.register(key("modal"), OverlayKind::Overlay, 10, true);
            let current_stack = stack.clone();
            let current_ran = current_ran.clone();
            window.defer(cx, move |_, _| {
                current_ran.set(current_stack.is_topmost(&current));
            });
        });
        cx.run_until_parked();
        assert!(!stale_ran.get());
        assert!(current_ran.get());
    }

    #[test]
    #[should_panic(expected = "overlay registration sequence exhausted")]
    fn registration_exhaustion_does_not_wrap_to_a_previous_token() {
        let stack = OverlayStack::default();
        stack.next_sequence.set(u64::MAX);
        stack.register(key("modal"), OverlayKind::Overlay, 10, true);
    }
}
