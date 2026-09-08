use super::*;
use crate::{native_workloads::WorkloadArena, semantic::*};
use std::cell::RefCell;

thread_local! { static EVENTS: RefCell<Vec<(u64, u16)>> = const { RefCell::new(Vec::new()) }; }
unsafe extern "C" fn capture(_: u64, token: u64, event: *const NativeControlEvent) -> i32 {
    EVENTS.with(|events| events.borrow_mut().push((token, unsafe { (*event).kind })));
    0
}

struct ShortcutView {
    native: Entity<ManagedView>,
    snapshot: ValidatedSnapshot,
    tabs: std::rc::Rc<std::cell::Cell<usize>>,
}
impl gpui::Render for ShortcutView {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let tabs = self.tabs.clone();
        self.native.update(cx, |native, cx| {
            native.overlay_stack.begin_frame();
            let content = native.materialize_node(0, &self.snapshot, window, cx);
            div()
                .size_full()
                .capture_key_down(cx.listener(|native, _, _, _| native.resources.shortcuts.begin()))
                .on_key_down(move |event, window, cx| {
                    if event.keystroke.key == "tab" {
                        tabs.set(tabs.get() + 1);
                        crate::app_host::cycle_focus(true, window, cx);
                        cx.stop_propagation();
                    }
                })
                .child(content)
        })
    }
}

fn snapshot(isolated: bool, options: u64, modal: bool) -> ValidatedSnapshot {
    let mut arena = WorkloadArena::default();
    let root = arena.node(COMPONENT_DIV, None);
    arena.op(root, OP_V_STACK, 0);
    // Primary-N: page command; backspace: must yield to native Input editing.
    arena.callback(root, OP_ON_SHORTCUT, 1, 14 | (32 << 16));
    arena.callback(root, OP_ON_SHORTCUT, 3, 19 | (32 << 16));
    arena.callback(root, OP_ON_SHORTCUT, 9, 40);
    let scope = if modal {
        let overlay = arena.node_with_data(COMPONENT_OVERLAY, Some(root), "dialog");
        arena.op(overlay, OP_RESOURCE_OWNER, 1);
        arena.op(overlay, OP_OVERLAY_MODAL, 1);
        overlay
    } else {
        arena.node(COMPONENT_DIV, Some(root))
    };
    arena.op(scope, OP_ISOLATE_SHORTCUTS, u64::from(isolated));
    arena.callback(scope, OP_ON_SHORTCUT, 2, 19 | (32 << 16) | options);
    let body = arena.node(COMPONENT_DIV, Some(scope));
    arena.op(body, OP_V_STACK, 0);
    let input = arena.node_with_data(COMPONENT_INPUT, Some(body), "edit\0hello\0");
    arena.op(input, OP_RESOURCE_OWNER, 1);
    arena.op(input, OP_INPUT_ON_CHANGED, 10);
    let button = arena.node_with_data(COMPONENT_BUTTON, Some(body), "next");
    arena.node_with_data(COMPONENT_TEXT, Some(button), "Next");
    arena.decode()
}
fn take() -> Vec<u64> {
    EVENTS.with(|events| {
        std::mem::take(&mut *events.borrow_mut())
            .into_iter()
            .filter(|(_, kind)| *kind == EVENT_SHORTCUT_INVOKED)
            .map(|(token, _)| token)
            .collect()
    })
}
fn draw(cx: &mut gpui::VisualTestContext) {
    cx.update(|window, cx| {
        window.refresh();
        window.draw(cx).clear(cx);
    });
}

#[gpui::test]
fn shortcut_scopes_route_commands_without_breaking_editing_or_tab(cx: &mut gpui::TestAppContext) {
    cx.update(|cx| {
        gpui_base::init(cx);
        crate::input::init(cx);
    });
    let native = cx.new(|_| {
        let mut callbacks = inert_callbacks();
        callbacks.control_event = Some(capture);
        ManagedView::new(1, callbacks, Default::default(), Default::default())
    });
    let tabs = std::rc::Rc::new(std::cell::Cell::new(0));
    let (view, cx) = cx.add_window_view(|_, _| ShortcutView {
        native,
        snapshot: snapshot(false, 0, false),
        tabs: tabs.clone(),
    });
    draw(cx);
    cx.simulate_mouse_down(
        point(px(20.), px(16.)),
        MouseButton::Left,
        gpui::Modifiers::none(),
    );
    cx.simulate_mouse_up(
        point(px(20.), px(16.)),
        MouseButton::Left,
        gpui::Modifiers::none(),
    );
    let primary_n = if cfg!(target_os = "macos") {
        "cmd-n"
    } else {
        "ctrl-n"
    };
    let primary_s = if cfg!(target_os = "macos") {
        "cmd-s"
    } else {
        "ctrl-s"
    };
    cx.simulate_keystrokes(primary_n);
    assert_eq!(take(), vec![1]);
    cx.simulate_keystrokes(primary_s);
    assert_eq!(take(), vec![2]);
    cx.simulate_keystrokes("backspace");
    assert!(
        take().is_empty(),
        "page shortcut intercepted native editing"
    );
    cx.simulate_keystrokes("a");
    assert!(
        EVENTS.with(|events| events
            .borrow()
            .iter()
            .any(|(_, kind)| *kind == EVENT_INPUT_CHANGED)),
        "ordinary typing did not reach native Input"
    );
    assert!(take().is_empty(), "page shortcut intercepted typed text");

    view.update(cx, |view, _| view.snapshot = snapshot(true, 0, false));
    draw(cx);
    cx.simulate_keystrokes(primary_n);
    assert!(take().is_empty(), "isolated scope leaked into page");
    cx.simulate_keystrokes(primary_s);
    assert_eq!(take(), vec![2]);
    cx.simulate_keystrokes("tab");
    assert_eq!(tabs.get(), 1, "isolation consumed unrelated Tab traversal");

    view.update(cx, |view, _| {
        view.snapshot = snapshot(false, 2 << 24, false)
    });
    draw(cx);
    cx.simulate_keystrokes(primary_s);
    assert!(take().is_empty(), "disabled binding invoked");
    cx.simulate_keystrokes(primary_n);
    assert_eq!(take(), vec![1], "isolation leaked into a later event/frame");

    view.update(cx, |view, _| {
        view.snapshot = snapshot(false, 1 << 24, false)
    });
    draw(cx);
    cx.simulate_keystrokes(primary_s);
    assert_eq!(
        take(),
        vec![2, 3],
        "non-consuming command did not propagate to parent"
    );

    view.update(cx, |view, _| view.snapshot = snapshot(false, 0, true));
    draw(cx);
    cx.run_until_parked();
    draw(cx);
    cx.simulate_keystrokes(primary_n);
    assert!(
        take().is_empty(),
        "modal overlay did not isolate page shortcuts"
    );
    cx.simulate_keystrokes(primary_s);
    assert_eq!(
        take(),
        vec![2],
        "modal command was unreachable from initial focus"
    );
}
