use super::*;

/// Key/mouse observers and shortcut declarations on one semantic node. Tokens are render-bound;
/// shortcut descriptors use payload word B, while observer payload words remain zero.
#[derive(Clone, Default)]
pub(super) struct KeyMouseBindings {
    pub(super) shortcuts: Vec<crate::shortcuts::Binding>,
    pub(super) isolate_shortcuts: bool,
    pub(super) key_down: u64,
    pub(super) key_up: u64,
    pub(super) mouse_down: u64,
    pub(super) mouse_up: u64,
    pub(super) modifiers_changed: u64,
    pub(super) hover: u64,
    pub(super) mouse_down_out: u64,
    pub(super) mouse_up_out: u64,
    pub(super) mouse_move: u64,
    pub(super) scroll_wheel: u64,
    pub(super) file_drop: u64,
}

pub(super) fn key_mouse_bindings(
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
) -> KeyMouseBindings {
    KeyMouseBindings {
        shortcuts: snapshot
            .ops(node)
            .iter()
            .filter(|op| op.code == crate::semantic::OP_ON_SHORTCUT)
            .map(|op| crate::shortcuts::Binding {
                token: op.a,
                descriptor: op.b,
            })
            .collect(),
        isolate_shortcuts: last_op(snapshot, node, crate::semantic::OP_ISOLATE_SHORTCUTS)
            .is_some_and(|op| op.a != 0),
        key_down: last_op(snapshot, node, OP_ON_KEY_DOWN).map_or(0, |op| op.a),
        key_up: last_op(snapshot, node, OP_ON_KEY_UP).map_or(0, |op| op.a),
        mouse_down: last_op(snapshot, node, OP_ON_MOUSE_DOWN).map_or(0, |op| op.a),
        mouse_up: last_op(snapshot, node, OP_ON_MOUSE_UP).map_or(0, |op| op.a),
        modifiers_changed: last_op(snapshot, node, OP_ON_MODIFIERS_CHANGED).map_or(0, |op| op.a),
        hover: last_op(snapshot, node, OP_ON_HOVER).map_or(0, |op| op.a),
        mouse_down_out: last_op(snapshot, node, OP_ON_MOUSE_DOWN_OUT).map_or(0, |op| op.a),
        mouse_up_out: last_op(snapshot, node, OP_ON_MOUSE_UP_OUT).map_or(0, |op| op.a),
        mouse_move: last_op(snapshot, node, OP_ON_MOUSE_MOVE).map_or(0, |op| op.a),
        scroll_wheel: last_op(snapshot, node, OP_ON_SCROLL_WHEEL).map_or(0, |op| op.a),
        file_drop: last_op(snapshot, node, OP_ON_FILE_DROP).map_or(0, |op| op.a),
    }
}

pub(super) fn modifiers_flags(modifiers: &gpui::Modifiers) -> u16 {
    u16::from(modifiers.control)
        | (u16::from(modifiers.alt) << 1)
        | (u16::from(modifiers.shift) << 2)
        | (u16::from(modifiers.platform) << 3)
        | (u16::from(modifiers.function) << 4)
}

pub(super) fn mouse_button_code(button: &MouseButton) -> u32 {
    match button {
        MouseButton::Left => 0,
        MouseButton::Right => 1,
        MouseButton::Middle => 2,
        MouseButton::Navigate(direction) => match direction {
            gpui::NavigationDirection::Back => 3,
            gpui::NavigationDirection::Forward => 4,
        },
    }
}

pub(super) fn invoke_key_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    kind: u16,
    key: &str,
    modifiers: u16,
    is_held: bool,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    let flags = modifiers | (u16::from(is_held) << 5);
    let bytes = key.as_bytes();
    let event = NativeControlEvent {
        kind,
        flags,
        reserved: 0,
        revision: 0,
        data: bytes.as_ptr(),
        data_length: bytes.len() as i32,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

pub(super) fn invoke_mouse_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    kind: u16,
    x: f32,
    y: f32,
    button: u32,
    click_count: u32,
    modifiers: u16,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    let mut payload = [0u8; 16];
    payload[..4].copy_from_slice(&x.to_bits().to_le_bytes());
    payload[4..8].copy_from_slice(&y.to_bits().to_le_bytes());
    payload[8..12].copy_from_slice(&button.to_le_bytes());
    payload[12..16].copy_from_slice(&click_count.to_le_bytes());
    let event = NativeControlEvent {
        kind,
        flags: modifiers,
        reserved: 0,
        revision: 0,
        data: payload.as_ptr(),
        data_length: payload.len() as i32,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

pub(super) fn invoke_modifiers_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    modifiers: u16,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    let event = NativeControlEvent {
        kind: EVENT_MODIFIERS_CHANGED,
        flags: modifiers,
        reserved: 0,
        revision: 0,
        data: std::ptr::null(),
        data_length: 0,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

pub(super) fn invoke_hover_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    modifiers: u16,
    is_hovering: bool,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    let event = NativeControlEvent {
        kind: EVENT_HOVER,
        flags: modifiers | (u16::from(is_hovering) << 5),
        reserved: 0,
        revision: 0,
        data: std::ptr::null(),
        data_length: 0,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

pub(super) fn invoke_mouse_move_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    x: f32,
    y: f32,
    pressed_button: Option<u32>,
    modifiers: u16,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    let mut payload = [0u8; 12];
    payload[..4].copy_from_slice(&x.to_bits().to_le_bytes());
    payload[4..8].copy_from_slice(&y.to_bits().to_le_bytes());
    payload[8..12].copy_from_slice(&pressed_button.unwrap_or(u32::MAX).to_le_bytes());
    let event = NativeControlEvent {
        kind: EVENT_MOUSE_MOVE,
        flags: modifiers,
        reserved: 0,
        revision: 0,
        data: payload.as_ptr(),
        data_length: payload.len() as i32,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

pub(super) fn invoke_scroll_wheel_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    x: f32,
    y: f32,
    delta_x: f32,
    delta_y: f32,
    units: u32,
    modifiers: u16,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    let mut payload = [0u8; 20];
    payload[..4].copy_from_slice(&x.to_bits().to_le_bytes());
    payload[4..8].copy_from_slice(&y.to_bits().to_le_bytes());
    payload[8..12].copy_from_slice(&delta_x.to_bits().to_le_bytes());
    payload[12..16].copy_from_slice(&delta_y.to_bits().to_le_bytes());
    payload[16..20].copy_from_slice(&units.to_le_bytes());
    let event = NativeControlEvent {
        kind: EVENT_SCROLL_WHEEL,
        flags: modifiers,
        reserved: 0,
        revision: 0,
        data: payload.as_ptr(),
        data_length: payload.len() as i32,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

pub(super) fn invoke_file_drop_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    x: f32,
    y: f32,
    paths: &[PathBuf],
    modifiers: u16,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    // 8-byte LE header (x, y) followed by NUL-separated lossy UTF-8 paths.
    // Paths never contain NUL, so the join is unambiguous.
    let mut payload = Vec::with_capacity(8 + paths.len() * 64);
    payload.extend_from_slice(&x.to_bits().to_le_bytes());
    payload.extend_from_slice(&y.to_bits().to_le_bytes());
    for (index, path) in paths.iter().enumerate() {
        if index != 0 {
            payload.push(0);
        }
        payload.extend_from_slice(path.to_string_lossy().as_bytes());
    }
    let event = NativeControlEvent {
        kind: EVENT_FILE_DROPPED,
        flags: modifiers,
        reserved: 0,
        revision: 0,
        data: payload.as_ptr(),
        data_length: payload.len() as i32,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

/// Shortcuts apply their native consumption policy; observer listeners remain non-consuming.
/// Both operate on the focus ancestry after descendant controls have handled the event.
pub(super) fn attach_key_mouse<T>(
    mut element: T,
    bindings: &KeyMouseBindings,
    cx: &mut Context<ManagedView>,
) -> T
where
    T: InteractiveElement,
{
    if !bindings.shortcuts.is_empty() || bindings.isolate_shortcuts {
        let shortcuts = bindings.shortcuts.clone();
        let isolated = bindings.isolate_shortcuts;
        element = element.on_key_down(cx.listener(move |this, event: &KeyDownEvent, _, cx| {
            let matched = this
                .resources
                .shortcuts
                .resolve(&shortcuts, isolated, event);
            if matched.consume {
                cx.stop_propagation();
            }
            if let Some(token) = matched.token {
                let event = NativeControlEvent {
                    kind: crate::semantic::EVENT_SHORTCUT_INVOKED,
                    flags: 0,
                    revision: 0,
                    data: std::ptr::null(),
                    data_length: 0,
                    reserved: 0,
                    reserved2: 0,
                };
                let status = unsafe {
                    this.callbacks.control_event.expect("validated callbacks")(
                        this.view_id,
                        token,
                        &event,
                    )
                };
                this.after_click(status, cx);
            }
        }));
    }
    if bindings.key_down != 0 {
        let token = bindings.key_down;
        element = element.on_key_down(cx.listener(move |this, event: &KeyDownEvent, _, cx| {
            let status = invoke_key_control_event(
                this.callbacks,
                this.view_id,
                token,
                EVENT_KEY_DOWN,
                event.keystroke.key.as_str(),
                modifiers_flags(&event.keystroke.modifiers),
                event.is_held,
            );
            this.after_click(status, cx);
        }));
    }
    if bindings.key_up != 0 {
        let token = bindings.key_up;
        element = element.on_key_up(cx.listener(move |this, event: &KeyUpEvent, _, cx| {
            let status = invoke_key_control_event(
                this.callbacks,
                this.view_id,
                token,
                EVENT_KEY_UP,
                event.keystroke.key.as_str(),
                modifiers_flags(&event.keystroke.modifiers),
                false,
            );
            this.after_click(status, cx);
        }));
    }
    if bindings.mouse_down != 0 {
        let token = bindings.mouse_down;
        for button in MouseButton::all() {
            let button_code = mouse_button_code(&button);
            element = element.on_mouse_down(
                button,
                cx.listener(move |this, event: &MouseDownEvent, _, cx| {
                    let status = invoke_mouse_control_event(
                        this.callbacks,
                        this.view_id,
                        token,
                        EVENT_MOUSE_DOWN,
                        event.position.x.into(),
                        event.position.y.into(),
                        button_code,
                        event.click_count.min(255) as u32,
                        modifiers_flags(&event.modifiers),
                    );
                    this.after_click(status, cx);
                }),
            );
        }
    }
    if bindings.mouse_up != 0 {
        let token = bindings.mouse_up;
        for button in MouseButton::all() {
            let button_code = mouse_button_code(&button);
            element = element.on_mouse_up(
                button,
                cx.listener(move |this, event: &MouseUpEvent, _, cx| {
                    let status = invoke_mouse_control_event(
                        this.callbacks,
                        this.view_id,
                        token,
                        EVENT_MOUSE_UP,
                        event.position.x.into(),
                        event.position.y.into(),
                        button_code,
                        event.click_count.min(255) as u32,
                        modifiers_flags(&event.modifiers),
                    );
                    this.after_click(status, cx);
                }),
            );
        }
    }
    if bindings.modifiers_changed != 0 {
        let token = bindings.modifiers_changed;
        element = element.on_modifiers_changed(cx.listener(
            move |this, event: &ModifiersChangedEvent, _, cx| {
                let status = invoke_modifiers_control_event(
                    this.callbacks,
                    this.view_id,
                    token,
                    modifiers_flags(&event.modifiers),
                );
                this.after_click(status, cx);
            },
        ));
    }
    if bindings.mouse_down_out != 0 {
        let token = bindings.mouse_down_out;
        element =
            element.on_mouse_down_out(cx.listener(move |this, event: &MouseDownEvent, _, cx| {
                let status = invoke_mouse_control_event(
                    this.callbacks,
                    this.view_id,
                    token,
                    EVENT_MOUSE_DOWN_OUT,
                    event.position.x.into(),
                    event.position.y.into(),
                    mouse_button_code(&event.button),
                    event.click_count.min(255) as u32,
                    modifiers_flags(&event.modifiers),
                );
                this.after_click(status, cx);
            }));
    }
    if bindings.mouse_up_out != 0 {
        let token = bindings.mouse_up_out;
        for button in MouseButton::all() {
            let button_code = mouse_button_code(&button);
            element = element.on_mouse_up_out(
                button,
                cx.listener(move |this, event: &MouseUpEvent, _, cx| {
                    let status = invoke_mouse_control_event(
                        this.callbacks,
                        this.view_id,
                        token,
                        EVENT_MOUSE_UP_OUT,
                        event.position.x.into(),
                        event.position.y.into(),
                        button_code,
                        event.click_count.min(255) as u32,
                        modifiers_flags(&event.modifiers),
                    );
                    this.after_click(status, cx);
                }),
            );
        }
    }
    if bindings.mouse_move != 0 {
        let token = bindings.mouse_move;
        // Opt-in only: without a binding no listener is installed and no
        // per-move crossing occurs. Handlers must stay cheap.
        element = element.on_mouse_move(cx.listener(move |this, event: &MouseMoveEvent, _, cx| {
            let status = invoke_mouse_move_control_event(
                this.callbacks,
                this.view_id,
                token,
                event.position.x.into(),
                event.position.y.into(),
                event.pressed_button.as_ref().map(mouse_button_code),
                modifiers_flags(&event.modifiers),
            );
            this.after_click(status, cx);
        }));
    }
    if bindings.scroll_wheel != 0 {
        let token = bindings.scroll_wheel;
        // Opt-in only: retained Scroll resources keep owning wheel deltas
        // natively; this observes without consuming.
        element =
            element.on_scroll_wheel(cx.listener(move |this, event: &ScrollWheelEvent, _, cx| {
                let (delta_x, delta_y, units) = match &event.delta {
                    gpui::ScrollDelta::Pixels(point) => (point.x.into(), point.y.into(), 0),
                    gpui::ScrollDelta::Lines(point) => (point.x, point.y, 1),
                };
                let status = invoke_scroll_wheel_control_event(
                    this.callbacks,
                    this.view_id,
                    token,
                    event.position.x.into(),
                    event.position.y.into(),
                    delta_x,
                    delta_y,
                    units,
                    modifiers_flags(&event.modifiers),
                );
                this.after_click(status, cx);
            }));
    }
    if bindings.file_drop != 0 {
        let token = bindings.file_drop;
        // GPUI translates platform file drops into its internal drag system, so
        // on_drop fires on the element under the cursor. Opt-in only.
        element = element.on_drop(cx.listener(move |this, paths: &ExternalPaths, window, cx| {
            let position = window.mouse_position();
            let status = invoke_file_drop_control_event(
                this.callbacks,
                this.view_id,
                token,
                position.x.into(),
                position.y.into(),
                paths.paths(),
                modifiers_flags(&window.modifiers()),
            );
            this.after_click(status, cx);
        }));
    }
    element
}

/// Attaches the hover observer. Hover tracking needs stable element state, so this
/// requires the stateful bound; plain Divs are wrapped with their deterministic node
/// id at the call site. Fires on enter/exit transitions only, never per mouse move.
pub(super) fn attach_hover<T>(
    mut element: T,
    bindings: &KeyMouseBindings,
    cx: &mut Context<ManagedView>,
) -> T
where
    T: StatefulInteractiveElement,
{
    if bindings.hover != 0 {
        let token = bindings.hover;
        element = element.on_hover(cx.listener(move |this, is_hovering: &bool, _, cx| {
            let status =
                invoke_hover_control_event(this.callbacks, this.view_id, token, 0, *is_hovering);
            this.after_click(status, cx);
        }));
    }
    element
}
