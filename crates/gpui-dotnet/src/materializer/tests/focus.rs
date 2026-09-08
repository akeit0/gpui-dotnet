use super::*;
use crate::{native_workloads::WorkloadArena, resources::ResourceCommand, semantic::*};
use std::cell::Cell;

thread_local! { static INVOKED: Cell<usize> = const { Cell::new(0) }; }
unsafe extern "C" fn capture(_: u64, _: u64, event: *const NativeControlEvent) -> i32 {
    if unsafe { (*event).kind } == EVENT_SHORTCUT_INVOKED {
        INVOKED.with(|count| count.set(count.get() + 1));
    }
    0
}

struct FocusView {
    native: Entity<ManagedView>,
    snapshot: ValidatedSnapshot,
}
impl gpui::Render for FocusView {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        self.native.update(cx, |native, cx| {
            native.resources.retain_snapshot(&self.snapshot);
            let content = native.materialize_node(0, &self.snapshot, window, cx);
            div()
                .size_full()
                .tab_group()
                .capture_key_down(cx.listener(|native, _, _, _| native.resources.shortcuts.begin()))
                .on_key_down(|event, window, cx| {
                    if event.keystroke.key == "tab" {
                        crate::app_host::cycle_focus(!event.keystroke.modifiers.shift, window, cx);
                        cx.stop_propagation();
                    }
                })
                .child(content)
        })
    }
}

fn snapshot(present: bool, tab_stop: bool, shifted: bool) -> ValidatedSnapshot {
    let mut arena = WorkloadArena::default();
    let root = arena.node(COMPONENT_DIV, None);
    arena.op(root, OP_V_STACK, 0);
    if shifted {
        arena.node(COMPONENT_DIV, Some(root));
    }
    if present {
        let panel = arena.node(COMPONENT_DIV, Some(root));
        arena.op(panel, OP_WIDTH_PX, 250f32.to_bits() as u64);
        arena.op(panel, OP_HEIGHT_PX, 100f32.to_bits() as u64);
        arena.op(panel, OP_RESOURCE_OWNER, 1);
        arena.data_op(panel, OP_FOCUS_TARGET, "panel");
        arena.op(panel, OP_FOCUS_TAB_STOP, u64::from(tab_stop));
        arena.callback(panel, OP_ON_SHORTCUT, 1, 43);
        let button = arena.node_with_data(COMPONENT_BUTTON, Some(panel), "child");
        arena.op(button, OP_WIDTH_PX, 80f32.to_bits() as u64);
        arena.op(button, OP_HEIGHT_PX, 30f32.to_bits() as u64);
    }
    let outside = arena.node_with_data(COMPONENT_INPUT, Some(root), "notes\0hello\0");
    arena.op(outside, OP_RESOURCE_OWNER, 1);
    arena.decode()
}
fn command(command: u16) -> ResourceCommand {
    ResourceCommand {
        key: crate::resources::ResourceKey::new(1, "panel".into()),
        resource_kind: RESOURCE_FOCUS,
        command,
        a: 0,
        b: 0,
        data: "".into(),
    }
}
fn draw(cx: &mut gpui::VisualTestContext) {
    cx.update(|window, cx| {
        window.refresh();
        window.draw(cx).clear(cx);
    });
}

#[gpui::test]
fn focus_targets_retain_identity_route_keys_and_respect_native_tab_and_pointer_focus(
    cx: &mut gpui::TestAppContext,
) {
    cx.update(|cx| {
        gpui_base::init(cx);
        crate::input::init(cx);
    });
    let native = cx.new(|_| {
        let mut callbacks = inert_callbacks();
        callbacks.control_event = Some(capture);
        ManagedView::new(1, callbacks, Default::default(), Default::default())
    });
    let mut presence = crate::presence::ResourcePresence::default();
    native.update(cx, |native, _| {
        native
            .resources
            .retain_snapshot(&snapshot(true, true, false));
        native.resources.publish_presence(&mut presence, 1);
        native.resources.dispatch(command(COMMAND_FOCUS_FOCUS));
    });
    assert_eq!(
        presence.base_generation(RESOURCE_FOCUS, &command(COMMAND_FOCUS_FOCUS).key),
        Some(1)
    );
    let (view, cx) = cx.add_window_view(|_, _| FocusView {
        native: native.clone(),
        snapshot: snapshot(true, true, false),
    });
    draw(cx);
    let focus = cx.update(|window, cx| window.focused(cx).unwrap());
    cx.simulate_keystrokes("left");
    assert_eq!(INVOKED.with(Cell::get), 1);

    view.update(cx, |view, _| view.snapshot = snapshot(true, true, true));
    draw(cx);
    cx.update(|window, _| {
        assert!(
            focus.is_focused(window),
            "reordering recreated focus identity"
        )
    });
    cx.simulate_keystrokes("tab");
    let child = cx.update(|window, cx| window.focused(cx).unwrap());
    assert_ne!(child, focus);
    native.update(cx, |native, _| {
        native.resources.dispatch(command(COMMAND_FOCUS_BLUR));
    });
    draw(cx);
    cx.update(|window, _| {
        assert!(
            child.is_focused(window),
            "parent Blur stole descendant focus"
        )
    });
    cx.simulate_keystrokes("shift-tab");
    cx.update(|window, _| assert!(focus.is_focused(window)));

    view.update(cx, |view, _| view.snapshot = snapshot(true, false, true));
    draw(cx);
    cx.update(|window, _| assert!(focus.is_focused(window), "tab policy changed identity"));
    cx.simulate_keystrokes("tab");
    cx.simulate_keystrokes("shift-tab");
    cx.update(|window, _| assert!(!focus.is_focused(window)));
    let before = INVOKED.with(Cell::get);
    cx.simulate_keystrokes("left");
    assert_eq!(
        INVOKED.with(Cell::get),
        before,
        "outside editing invoked panel shortcut"
    );

    // Descendant pointer focus wins over the focusable container.
    cx.simulate_mouse_down(
        point(px(20.), px(15.)),
        MouseButton::Left,
        gpui::Modifiers::none(),
    );
    cx.simulate_mouse_up(
        point(px(20.), px(15.)),
        MouseButton::Left,
        gpui::Modifiers::none(),
    );
    cx.update(|window, _| assert!(child.is_focused(window)));
    cx.simulate_mouse_down(
        point(px(180.), px(70.)),
        MouseButton::Left,
        gpui::Modifiers::none(),
    );
    cx.simulate_mouse_up(
        point(px(180.), px(70.)),
        MouseButton::Left,
        gpui::Modifiers::none(),
    );
    cx.update(|window, _| {
        assert!(
            focus.is_focused(window),
            "pointer cannot focus non-tab-stop target"
        )
    });
    native.update(cx, |native, _| {
        native.resources.dispatch(command(COMMAND_FOCUS_BLUR));
    });
    draw(cx);
    cx.update(|window, _| assert!(!focus.is_focused(window)));

    // Removal prunes queued commands and identity, even while an old frame retains the handle.
    native.update(cx, |native, _| {
        native.resources.dispatch(command(COMMAND_FOCUS_FOCUS));
    });
    view.update(cx, |view, _| view.snapshot = snapshot(false, false, false));
    draw(cx);
    let mut presence = crate::presence::ResourcePresence::default();
    native.update(cx, |native, _| {
        native.resources.publish_presence(&mut presence, 2)
    });
    assert_eq!(
        presence.base_generation(RESOURCE_FOCUS, &command(50).key),
        None
    );
    view.update(cx, |view, _| view.snapshot = snapshot(true, true, false));
    draw(cx);
    cx.update(|window, _| assert!(!focus.is_focused(window)));
    native.update(cx, |native, _| {
        native.resources.dispatch(command(COMMAND_FOCUS_FOCUS));
    });
    draw(cx);
    let replacement = cx.update(|window, cx| window.focused(cx).unwrap());
    assert_ne!(replacement, focus);
}

#[test]
fn focus_declarations_reject_missing_owners_duplicate_targets_and_duplicate_keys() {
    for mode in 0..4 {
        let mut arena = WorkloadArena::default();
        let root = arena.node(COMPONENT_DIV, None);
        let node = arena.node(COMPONENT_DIV, Some(root));
        arena.data_op(node, OP_FOCUS_TARGET, "panel");
        if mode != 0 {
            arena.op(node, OP_RESOURCE_OWNER, 1);
        }
        if mode == 1 {
            arena.data_op(node, OP_FOCUS_TARGET, "second");
        }
        if mode == 2 {
            let second = arena.node(COMPONENT_DIV, Some(root));
            arena.op(second, OP_RESOURCE_OWNER, 1);
            arena.data_op(second, OP_FOCUS_TARGET, "panel");
        }
        if mode == 3 {
            arena.op(root, OP_FOCUS_TAB_STOP, 1);
        }
        let expected = if mode == 2 { -56 } else { -67 };
        assert_eq!(
            arena.decode_into(&mut ValidatedSnapshot::default()),
            Err(expected)
        );
    }
}
