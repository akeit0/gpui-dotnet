use crate::abi::{ManagedCallbacks, NativeControlEvent};
use crate::collections::{
    CollectionEngine, ListConfiguration, ListItemEventKind, ListOrientation, TableColumnSpec,
    TableSpec,
    configuration::{pack_table_column, parse_table_spec, shared, unpack_table_column},
    engine::CachedBatch,
};
use crate::resources::{ResourceCommand, ResourceKey, ResourceStore};
use crate::scrolling::{DEFAULT_SCROLLBAR_WIDTH, ScrollbarMetrics};
use crate::semantic::{
    COMMAND_LIST_REFRESH, COMMAND_LIST_RESET, COMMAND_LIST_SPLICE, EVENT_LIST_ACTIVATED,
    EVENT_LIST_SELECTION_REQUESTED, OP_LIST_ITEM_ID, OP_RESOURCE_OWNER, RESOURCE_LIST,
};
use crate::snapshot::{RetainedStrings, SnapshotScratch, ValidatedSnapshot};
use crate::theme::SharedTheme;
use gpui::{
    Context, Entity, IntoElement, ListAlignment, ListOffset, ParentElement, Window, div, point, px,
};
use std::cell::{Cell, RefCell};
use std::collections::HashSet;
use std::rc::Rc;
mod horizontal;
mod item_tooltips;
mod measurements;

#[derive(Default)]
struct ArtifactCapture {
    nodes: Vec<crate::abi::NodeRecord>,
    children: Vec<crate::abi::ChildRecord>,
    ops: Vec<crate::abi::OpRecord>,
    clickable: bool,
    item_ids: bool,
    tooltip_targets: bool,
    ranges: Vec<(u32, u32)>,
    activations: Vec<(u64, u16, u64, Vec<u8>)>,
    activation_resource: Option<std::rc::Weak<RefCell<CollectionEngine>>>,
    clicked: Vec<u64>,
    click_status: i32,
    next_id: u64,
    requests: Vec<(u64, u64)>,
    accepts: Vec<(u64, u64)>,
    releases: Vec<(u64, u64, i32)>,
    failure_mode: u8,
}

thread_local! {
    static ARTIFACTS: RefCell<ArtifactCapture> = RefCell::default();
}

unsafe extern "C" fn publish_test_range(
    _: u64,
    _: u64,
    source: u64,
    start: u32,
    count: u32,
    arena: *mut crate::abi::RenderArena,
    root: *mut u32,
    artifact: *mut u64,
) -> i32 {
    use crate::{
        abi::{ChildRecord, NodeRecord},
        semantic::{COMPONENT_DIV, COMPONENT_TEXT},
    };
    ARTIFACTS.with(|capture| {
        let mut capture = capture.borrow_mut();
        if capture.failure_mode == 3 {
            return -106;
        }
        let items = if capture.failure_mode == 2 { 0 } else { count };
        capture.nodes.clear();
        let component = if capture.failure_mode == 1 {
            u16::MAX
        } else {
            COMPONENT_DIV
        };
        capture.nodes.push(NodeRecord {
            component,
            ..Default::default()
        });
        capture.children.clear();
        for index in 0..items {
            capture.nodes.push(NodeRecord {
                component: COMPONENT_TEXT,
                ..Default::default()
            });
            capture.children.push(ChildRecord {
                parent: 0,
                child: index + 1,
            });
        }
        capture.next_id += 1;
        let id = capture.next_id;
        capture.ops.clear();
        if capture.clickable {
            use crate::semantic::{OP_HEIGHT_PX, OP_ON_CLICK, OP_WIDTH_PX, ValueKind};
            for index in 0..items {
                capture.nodes[index as usize + 1].component = crate::semantic::COMPONENT_BUTTON;
                capture.nodes[index as usize + 1].data_length = 3;
                capture.ops.extend([
                    crate::abi::OpRecord {
                        node: index + 1,
                        code: OP_WIDTH_PX,
                        value_kind: ValueKind::F32 as u16,
                        a: 200f32.to_bits() as u64,
                        b: 0,
                    },
                    crate::abi::OpRecord {
                        node: index + 1,
                        code: OP_ON_CLICK,
                        value_kind: ValueKind::Callback as u16,
                        a: id,
                        b: 0,
                    },
                    crate::abi::OpRecord {
                        node: index + 1,
                        code: OP_HEIGHT_PX,
                        value_kind: ValueKind::F32 as u16,
                        a: 30f32.to_bits() as u64,
                        b: 0,
                    },
                ]);
            }
        }
        if capture.tooltip_targets {
            for index in 0..items {
                for (code, value_kind, a) in [
                    (
                        crate::semantic::OP_ITEM_TOOLTIP_TARGET,
                        crate::semantic::ValueKind::U32,
                        1,
                    ),
                    (
                        crate::semantic::OP_WIDTH_PX,
                        crate::semantic::ValueKind::F32,
                        200f32.to_bits() as u64,
                    ),
                    (
                        crate::semantic::OP_HEIGHT_PX,
                        crate::semantic::ValueKind::F32,
                        40f32.to_bits() as u64,
                    ),
                ] {
                    capture.ops.push(crate::abi::OpRecord {
                        node: index + 1,
                        code,
                        value_kind: value_kind as u16,
                        a,
                        b: 0,
                    });
                }
            }
        }
        if capture.item_ids {
            for index in 0..items {
                capture.ops.push(crate::abi::OpRecord {
                    node: index + 1,
                    code: OP_LIST_ITEM_ID,
                    value_kind: crate::semantic::ValueKind::U64 as u16,
                    a: 1000 + (start + index) as u64,
                    b: 0,
                });
            }
        }
        capture.ranges.push((start, count));
        capture.requests.push((source, id));
        unsafe {
            if capture.clickable {
                (*arena).utf8 = b"row".as_ptr().cast_mut();
                (*arena).utf8_length = 3;
                (*arena).utf8_capacity = 3;
            }
            (*arena).ops = capture.ops.as_mut_ptr();
            (*arena).op_length = capture.ops.len() as i32;
            (*arena).op_capacity = capture.ops.capacity() as i32;
            (*arena).nodes = capture.nodes.as_mut_ptr();
            (*arena).node_length = capture.nodes.len() as i32;
            (*arena).node_capacity = capture.nodes.capacity() as i32;
            (*arena).children = capture.children.as_mut_ptr();
            (*arena).child_length = capture.children.len() as i32;
            (*arena).child_capacity = capture.children.capacity() as i32;
            (*arena).generation = 1;
            *root = 0;
            *artifact = id;
        }
        0
    })
}

unsafe extern "C" fn release_test_artifact(_: u64, source: u64, artifact: u64, status: i32) -> i32 {
    ARTIFACTS.with(|capture| {
        capture
            .borrow_mut()
            .releases
            .push((source, artifact, status))
    });
    0
}

unsafe extern "C" fn capture_activation(
    _: u64,
    token: u64,
    event: *const NativeControlEvent,
) -> i32 {
    let event = unsafe { &*event };
    let bytes = unsafe { std::slice::from_raw_parts(event.data, event.data_length as usize) };
    ARTIFACTS.with(|capture| {
        let mut capture = capture.borrow_mut();
        assert_eq!(
            event.kind,
            if token == 42 {
                EVENT_LIST_ACTIVATED
            } else {
                EVENT_LIST_SELECTION_REQUESTED
            }
        );
        assert_eq!(event.reserved, 0);
        assert_eq!(event.reserved2, 0);
        if let Some(resource) = capture
            .activation_resource
            .as_ref()
            .and_then(|weak| weak.upgrade())
        {
            assert!(
                resource.try_borrow_mut().is_ok(),
                "resource borrow escaped into activation callback"
            );
        }
        capture
            .activations
            .push((token, event.flags, event.revision, bytes.to_vec()));
    });
    0
}

#[gpui::test]
fn list_activation_resolves_uncached_identity_in_one_batch_and_ignores_repeat_and_modifiers(
    cx: &mut gpui::TestAppContext,
) {
    check_collection_key_event(cx, ListItemEventKind::Activation);
}

#[gpui::test]
fn list_selection_resolves_uncached_identity_without_selecting_on_navigation_or_repeat(
    cx: &mut gpui::TestAppContext,
) {
    check_collection_key_event(cx, ListItemEventKind::Selection);
}

fn check_collection_key_event(cx: &mut gpui::TestAppContext, kind: ListItemEventKind) {
    ARTIFACTS.with(|capture| {
        *capture.borrow_mut() = ArtifactCapture {
            item_ids: true,
            ..Default::default()
        }
    });
    let mut config = configuration(Some(0));
    let (key, token) = match kind {
        ListItemEventKind::Activation => {
            config.activation_token = 42;
            ("enter", 42)
        }
        ListItemEventKind::Selection => {
            config.selection_token = 43;
            ("space", 43)
        }
    };
    let callbacks = ManagedCallbacks {
        control_event: Some(capture_activation),
        ..artifact_callbacks()
    };
    let resource = Rc::new(RefCell::new(CollectionEngine::new(
        1, callbacks, &config, 1,
    )));
    resource.borrow().cursor.set(51);
    ARTIFACTS
        .with(|capture| capture.borrow_mut().activation_resource = Some(Rc::downgrade(&resource)));
    let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
    cx.update(|window, cx| {
        let focus = cx.focus_handle();
        focus.focus(window, cx);
        let mut event = gpui::KeyDownEvent {
            keystroke: gpui::Keystroke::parse("down").unwrap(),
            is_held: false,
            prefer_character_input: false,
        };
        assert!(!crate::materializer::handle_collection_item_event_key(
            &event, window, cx, &focus, &resource
        ));
        event.keystroke =
            gpui::Keystroke::parse(if key == "enter" { "space" } else { "enter" }).unwrap();
        assert!(!crate::materializer::handle_collection_item_event_key(
            &event, window, cx, &focus, &resource
        ));
        ARTIFACTS.with_borrow(|capture| assert!(capture.ranges.is_empty()));
        event.keystroke = gpui::Keystroke::parse(key).unwrap();
        assert!(crate::materializer::handle_collection_item_event_key(
            &event, window, cx, &focus, &resource
        ));
        event.is_held = true;
        assert!(crate::materializer::handle_collection_item_event_key(
            &event, window, cx, &focus, &resource
        ));
        event.is_held = false;
        event.keystroke.modifiers.shift = true;
        assert!(!crate::materializer::handle_collection_item_event_key(
            &event, window, cx, &focus, &resource
        ));
        event.keystroke.modifiers.shift = false;
        event.prefer_character_input = true;
        assert!(!crate::materializer::handle_collection_item_event_key(
            &event, window, cx, &focus, &resource
        ));
        event.prefer_character_input = false;
        let child_focus = cx.focus_handle();
        child_focus.focus(window, cx);
        assert!(!crate::materializer::handle_collection_item_event_key(
            &event, window, cx, &focus, &resource
        ));
    });
    ARTIFACTS.with(|capture| {
        let capture = capture.borrow();
        assert_eq!(capture.ranges, vec![(48, 48)]);
        assert_eq!(capture.accepts.len(), 1);
        assert_eq!(capture.activations.len(), 1);
        let (actual_token, flags, revision, bytes) = &capture.activations[0];
        assert_eq!((*actual_token, *flags, *revision), (token, 3, 0));
        assert_eq!(&bytes[..4], &51u32.to_le_bytes());
        assert_eq!(&bytes[4..8], &[0; 4]);
        assert_eq!(&bytes[8..], &1051u64.to_le_bytes());
    });
    let event = resource
        .borrow_mut()
        .prepare_item_event(51, kind)
        .unwrap()
        .unwrap();
    assert_eq!(event.emit(kind, false), 0);
    ARTIFACTS.with(|capture| {
        let capture = capture.borrow();
        assert_eq!(capture.ranges.len(), 1);
        assert_eq!(capture.activations[1].1, 2);
    });
}

#[test]
fn list_activation_is_opt_in_and_rejects_failed_or_out_of_range_rows() {
    ARTIFACTS.with(|capture| *capture.borrow_mut() = ArtifactCapture::default());
    let mut config = configuration(None);
    let mut resource = CollectionEngine::new(1, artifact_callbacks(), &config, 1);
    assert!(
        resource
            .prepare_item_event(50, ListItemEventKind::Activation)
            .unwrap()
            .is_none()
    );
    ARTIFACTS.with(|capture| assert!(capture.borrow().ranges.is_empty()));
    config.activation_token = 42;
    let epoch = resource.cursor.epoch();
    resource.configure(&config, 2);
    assert_ne!(resource.cursor.epoch(), epoch);
    assert!(
        resource
            .prepare_item_event(100, ListItemEventKind::Activation)
            .unwrap()
            .is_none()
    );
    let event = resource
        .prepare_item_event(0, ListItemEventKind::Activation)
        .unwrap()
        .unwrap();
    assert_eq!(event.item_id, None);
    assert_eq!(event.content_revision, None);
    resource.clear_batches();
    ARTIFACTS.with(|capture| capture.borrow_mut().failure_mode = 3);
    assert!(matches!(
        resource.prepare_item_event(51, ListItemEventKind::Activation),
        Err((1, -106))
    ));
    assert!(resource.cached_item_events(51).is_none());
}

#[test]
fn list_selection_binding_changes_revoke_old_rows_without_loading_or_resetting_cursor() {
    ARTIFACTS.with(|capture| *capture.borrow_mut() = ArtifactCapture::default());
    let mut config = configuration(Some(1));
    config.activation_token = 42;
    let mut resource = CollectionEngine::new(1, artifact_callbacks(), &config, 1);
    resource.cursor.set(51);
    assert!(
        resource
            .prepare_item_event(51, ListItemEventKind::Selection)
            .unwrap()
            .is_none()
    );
    ARTIFACTS.with_borrow(|capture| assert!(capture.ranges.is_empty()));
    let epoch = resource.cursor.epoch();
    config.selection_token = 43;
    resource.configure(&config, 2);
    assert_ne!(resource.cursor.epoch(), epoch);
    let packet = resource
        .prepare_item_event(51, ListItemEventKind::Selection)
        .unwrap()
        .unwrap();
    assert_eq!(packet.selection_token, 43);
    assert_eq!(packet.activation_token, 42);
    assert_eq!(resource.cursor.active(), Some(51));
    let epoch = resource.cursor.epoch();
    config.selection_token = 0;
    resource.configure(&config, 3);
    assert!(!resource.cursor.set_from_item(12, epoch));
    assert_eq!(resource.cursor.active(), Some(51));
    assert!(
        resource
            .prepare_item_event(51, ListItemEventKind::Selection)
            .unwrap()
            .is_none()
    );
    assert_eq!(resource.cached_item_events(51).unwrap().selection_token, 0);
    ARTIFACTS.with_borrow(|capture| assert_eq!(capture.ranges, vec![(48, 48)]));
    config.selection_token = 43;
    config.item_count = 0;
    resource.configure(&config, 4);
    assert!(resource.cursor.active().is_none());
    assert!(
        resource
            .prepare_item_event(0, ListItemEventKind::Selection)
            .unwrap()
            .is_none()
    );
    ARTIFACTS.with_borrow(|capture| assert_eq!(capture.ranges.len(), 1));
}

fn artifact_callbacks() -> ManagedCallbacks {
    ManagedCallbacks {
        click: Some(click_test_row),
        accept_artifact: Some(accept_test_artifact),
        list_render_range: Some(publish_test_range),
        release_artifact: Some(release_test_artifact),
        ..callbacks()
    }
}

unsafe extern "C" fn click_test_row(
    _: u64,
    token: u64,
    _: u64,
    _: *const crate::abi::NativeClickEvent,
) -> i32 {
    ARTIFACTS.with(|capture| {
        let mut capture = capture.borrow_mut();
        if !capture
            .releases
            .iter()
            .any(|(_, artifact, _)| *artifact == token)
        {
            capture.clicked.push(token);
        }
        capture.click_status
    })
}

struct ItemMenuView {
    store: Rc<ResourceStore>,
    resource: Rc<RefCell<CollectionEngine>>,
    focus: gpui::FocusHandle,
    paints: Rc<Cell<usize>>,
    show_items: bool,
    show_menu: bool,
}

impl gpui::Render for ItemMenuView {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        use gpui::Styled;
        self.paints.set(0);
        self.store.item_menus.begin_frame(window, cx);
        self.resource.borrow_mut().begin_frame();
        let resource = self.resource.clone();
        let store = self.store.clone();
        let cursor = resource.borrow().cursor.clone();
        let focus = self.focus.clone();
        let mut root = div().flex().flex_col().w(px(300.)).h(px(240.));
        if self.show_items {
            let state = resource.borrow().state.clone();
            let items = gpui::list(state, move |index, _, _| {
                let item = resource.borrow_mut().render_item(
                    index,
                    &store,
                    &ResourceKey::new(1, "rows".into()),
                );
                crate::materializer::CollectionItem::new(
                    div().h(px(40.)).w_full().child(item).into_any_element(),
                    cursor.clone(),
                    focus.clone(),
                    index,
                )
                .with_context_menu(
                    (1u64 << 32) | 44,
                    store.item_menus.clone(),
                    resource.clone(),
                )
                .into_any_element()
            })
            .size_full();
            root = root.child(items);
        }
        let request = ARTIFACTS.with(|capture| {
            capture
                .borrow()
                .activations
                .last()
                .map(|event| u64::from_le_bytes(event.3[16..24].try_into().unwrap()))
        });
        if self.show_menu
            && let Some(id) = request
        {
            let stack = crate::overlay::OverlayStack::new();
            let key = ResourceKey::new(1, "row-menu".into());
            let token = stack.register(
                key.clone(),
                crate::overlay::OverlayKind::ContextMenu,
                300,
                false,
            );
            let paints = self.paints.clone();
            let content = div().w(px(100.)).h(px(80.)).child(gpui::canvas(
                |_, _, _| (),
                move |_, _, _, _| paints.set(paints.get() + 1),
            ));
            root = root.child(crate::context_menu::context_menu(
                key,
                div(),
                div().into_any_element(),
                content.into_any_element(),
                crate::context_menu::ContextMenuConfiguration {
                    priority: 300,
                    margin: 8.,
                },
                stack,
                token,
                Some((self.store.item_menus.clone(), id)),
                window,
                cx,
            ));
        }
        self.store.item_menus.finish_declarations(window, cx);
        root
    }
}

unsafe extern "C" fn capture_item_menu(
    _: u64,
    token: u64,
    event: *const NativeControlEvent,
) -> i32 {
    let event = unsafe { &*event };
    let bytes = unsafe { std::slice::from_raw_parts(event.data, event.data_length as usize) };
    ARTIFACTS.with(|capture| {
        capture
            .borrow_mut()
            .activations
            .push((token, event.flags, event.revision, bytes.to_vec()))
    });
    0
}

#[gpui::test]
fn item_context_menu_uses_stable_identity_and_expires_with_its_displayed_anchor(
    cx: &mut gpui::TestAppContext,
) {
    cx.update(gpui_base::init);
    for change in 0..9 {
        ARTIFACTS.with(|capture| {
            *capture.borrow_mut() = ArtifactCapture {
                item_ids: true,
                ..Default::default()
            }
        });
        let callbacks = ManagedCallbacks {
            control_event: Some(capture_item_menu),
            ..artifact_callbacks()
        };
        let store = Rc::new(ResourceStore::new(1, callbacks, theme()));
        let mut config = configuration(Some(7));
        config.item_count = 20;
        let resource = Rc::new(RefCell::new(CollectionEngine::new(
            1, callbacks, &config, 1,
        )));
        let paints = Rc::new(Cell::new(0));
        let (view, cx) = cx.add_window_view(|_, cx| ItemMenuView {
            store,
            resource: resource.clone(),
            focus: cx.focus_handle(),
            paints: paints.clone(),
            show_items: true,
            show_menu: true,
        });
        let draw = |cx: &mut gpui::VisualTestContext| {
            cx.update(|window, cx| {
                window.refresh();
                window.draw(cx).clear(cx);
            });
        };
        draw(cx);
        cx.simulate_mouse_down(
            point(px(50.), px(60.)),
            gpui::MouseButton::Right,
            gpui::Modifiers::none(),
        );
        draw(cx);
        assert_eq!(paints.get(), 1, "menu was not painted");
        // Ordinary root renders replace callback tokens without replacing item identity.
        resource.borrow().cursor.invalidate_items();
        draw(cx);
        assert_eq!(paints.get(), 1, "rebinding item events dismissed the menu");
        ARTIFACTS.with(|capture| {
            let capture = capture.borrow();
            let (token, flags, revision, bytes) = capture.activations.last().unwrap();
            assert_eq!((*token, *flags, *revision), ((1u64 << 32) | 44, 2, 7));
            assert_eq!(u32::from_le_bytes(bytes[..4].try_into().unwrap()), 1);
            assert_eq!(u64::from_le_bytes(bytes[8..16].try_into().unwrap()), 1001);
        });
        match change {
            0 => resource.borrow_mut().scroll_to_item(19),
            1 => resource.borrow_mut().clear_batches(),
            2 => {
                config.projection_revision = Some(2);
                resource.borrow_mut().configure(&config, 2);
            }
            3 => view.update(cx, |view, _| view.show_items = false),
            4 => view.update(cx, |view, _| view.show_menu = false),
            5 => cx.simulate_keystrokes("escape"),
            6 => cx.simulate_click(point(px(290.), px(220.)), Default::default()),
            7 => {
                cx.simulate_event(gpui::ScrollWheelEvent {
                    position: point(px(100.), px(100.)),
                    delta: gpui::ScrollDelta::Pixels(point(px(0.), px(-40.))),
                    ..Default::default()
                });
            }
            _ => {
                config.content_revision = Some(8);
                resource.borrow_mut().configure(&config, 2);
            }
        }
        draw(cx);
        assert_eq!(paints.get(), 0, "menu survived change {change}");
        view.update(cx, |view, _| {
            view.show_items = true;
            view.show_menu = true;
        });
        draw(cx);
        assert_eq!(
            paints.get(),
            0,
            "expired request reopened after change {change}"
        );
    }
}

struct VisibleItems {
    store: Rc<ResourceStore>,
    resource: Rc<RefCell<CollectionEngine>>,
}

impl gpui::Render for VisibleItems {
    fn render(&mut self, _: &mut Window, _: &mut Context<Self>) -> impl IntoElement {
        use gpui::Styled;
        self.resource.borrow_mut().begin_frame();
        let key = ResourceKey::new(1, "rows".into());
        let state = self.resource.borrow().state.clone();
        let items_resource = self.resource.clone();
        let store = self.store.clone();
        let items = div().flex().flex_col().w(px(200.)).h(px(240.)).child(
            gpui::list(state, move |index, _, _| {
                items_resource.borrow_mut().render_item(index, &store, &key)
            })
            .size_full(),
        );
        let resource = self.resource.clone();
        items.child(gpui::canvas(
            move |_, _, _| resource.borrow_mut().trim_batches(),
            |_, _, _, _| {},
        ))
    }
}

#[gpui::test]
fn displayed_rows_keep_artifacts_after_prepaint(cx: &mut gpui::TestAppContext) {
    cx.update(gpui_base::init);
    ARTIFACTS.with(|capture| {
        *capture.borrow_mut() = ArtifactCapture {
            clickable: true,
            ..Default::default()
        }
    });
    let store = Rc::new(ResourceStore::new(1, artifact_callbacks(), theme()));
    let mut config = configuration(Some(1));
    config.item_count = 8;
    config.batch_size = 1;
    config.overdraw = px(0.);
    config.estimated_item_extent = px(30.);
    let resource = Rc::new(RefCell::new(CollectionEngine::new(
        1,
        artifact_callbacks(),
        &config,
        1,
    )));
    let (_, cx) = cx.add_window_view(|_, _| VisibleItems {
        store,
        resource: resource.clone(),
    });
    cx.update(|window, cx| {
        window.draw(cx).clear(cx);
    });
    ARTIFACTS.with(|capture| {
        assert_eq!(
            resource.borrow().batches.len(),
            8,
            "requests={:?}, releases={:?}",
            capture.borrow().requests,
            capture.borrow().releases
        )
    });
    ARTIFACTS.with(|capture| assert!(capture.borrow().releases.is_empty()));
    cx.simulate_click(point(px(10.), px(15.)), Default::default());
    cx.simulate_click(point(px(10.), px(225.)), Default::default());
    ARTIFACTS.with(|capture| assert_eq!(capture.borrow().clicked, [1, 8]));
    // Once no frame needs these batches, only four idle batches remain cached.
    resource.borrow_mut().begin_frame();
    resource.borrow_mut().trim_batches();
    assert_eq!(resource.borrow().batches.len(), 4);
    resource.borrow_mut().clear_batches();
    ARTIFACTS.with(|capture| assert_eq!(capture.borrow().releases.len(), 8));
}

unsafe extern "C" fn accept_test_artifact(_: u64, source: u64, artifact: u64) -> i32 {
    ARTIFACTS.with(|capture| {
        let mut capture = capture.borrow_mut();
        capture.accepts.push((source, artifact));
        // Acceptance can reuse the managed output: native decoding must already be done.
        capture.nodes.clear();
        capture.children.clear();
        if capture.failure_mode == 4 { -109 } else { 0 }
    })
}

#[test]
fn artifact_acceptance_follows_decode_and_rejection_releases_the_batch() {
    for mode in 0..=4 {
        ARTIFACTS.with(|capture| {
            *capture.borrow_mut() = ArtifactCapture {
                failure_mode: mode,
                ..Default::default()
            }
        });
        let mut resource = artifact_resource();
        assert_eq!(resource.load_batch(0).is_ok(), mode == 0);
        if mode == 0 {
            assert_eq!(resource.batches[&0].snapshot.nodes.len(), 2);
        }
        ARTIFACTS.with(|capture| {
            let capture = capture.borrow();
            assert_eq!(capture.accepts.len(), usize::from(mode == 0 || mode == 4));
            if mode == 4 {
                assert_eq!(capture.releases.len(), 1);
            }
        });
    }
}

#[test]
fn cached_snapshots_survive_reused_scratch_growth_and_failed_batches() {
    ARTIFACTS.with(|capture| {
        *capture.borrow_mut() = ArtifactCapture {
            clickable: true,
            ..Default::default()
        };
    });
    let mut config = configuration(Some(1));
    config.item_count = 2_000;
    let mut resource = CollectionEngine::new(1, artifact_callbacks(), &config, 1);
    resource.load_batch(0).unwrap();
    let first_id = resource.batches[&0].lease.as_ref().unwrap().artifact_id;

    ARTIFACTS.with(|capture| capture.borrow_mut().clickable = false);
    resource.batch_size = 1;
    resource.load_batch(48).unwrap();
    ARTIFACTS.with(|capture| capture.borrow_mut().failure_mode = 2);
    assert_eq!(resource.load_batch(49), Err(-63));
    ARTIFACTS.with(|capture| {
        let mut capture = capture.borrow_mut();
        capture.failure_mode = 0;
        capture.clickable = true;
    });
    resource.batch_size = 512;
    resource.load_batch(512).unwrap();

    let first = &resource.batches[&0].snapshot;
    assert_eq!(first.children(&first.nodes[0]).len(), 48);
    assert!(first.nodes[1..].iter().all(|node| {
        node.component == crate::semantic::COMPONENT_BUTTON
            && node.data.as_ref() == "row"
            && first
                .ops(node)
                .iter()
                .any(|op| op.code == crate::semantic::OP_ON_CLICK && op.a == first_id)
    }));
    let second = &resource.batches[&48].snapshot;
    assert_eq!(second.nodes.len(), 2);
    assert_eq!(second.nodes[1].component, crate::semantic::COMPONENT_TEXT);
    assert!(second.ops(&second.nodes[1]).is_empty());
    assert_eq!(resource.batches[&512].snapshot.nodes.len(), 513);
    assert!(
        resource.invalidate_sorted_artifacts(&[crate::abi::NativeArtifactKey {
            source: resource.source_id,
            artifact: first_id,
        }])
    );
    assert_eq!(batch_keys(&resource), vec![48, 512]);
    ARTIFACTS.with(|capture| {
        assert_eq!(
            capture.borrow().releases,
            vec![
                (resource.source_id, 3, -63),
                (resource.source_id, first_id, 0)
            ]
        );
    });
}

#[test]
fn reactive_invalidation_evicts_only_matching_source_and_artifact() {
    ARTIFACTS.with(|capture| *capture.borrow_mut() = ArtifactCapture::default());
    let mut resource = artifact_resource();
    resource.load_batch(0).unwrap();
    resource.load_batch(1).unwrap();
    let key = crate::abi::NativeArtifactKey {
        source: resource.source_id,
        artifact: 1,
    };
    assert!(
        !resource.invalidate_sorted_artifacts(&[crate::abi::NativeArtifactKey {
            source: key.source + 1,
            ..key
        }])
    );
    assert!(resource.invalidate_sorted_artifacts(&[key]));
    assert!(resource.batches.contains_key(&1));
    resource.load_batch(0).unwrap();
    assert!(!resource.invalidate_sorted_artifacts(&[key]));
    assert_eq!(resource.batches.len(), 2);
    ARTIFACTS.with(|capture| assert_eq!(capture.borrow().releases, vec![(key.source, 1, 0)]));
}

fn artifact_resource() -> CollectionEngine {
    let mut config = configuration(Some(1));
    config.batch_size = 1;
    CollectionEngine::new(1, artifact_callbacks(), &config, 1)
}

#[test]
fn invalidation_batch_handles_unsorted_duplicates_and_overlapping_artifact_ids() {
    use crate::abi::NativeArtifactKey;

    ARTIFACTS.with(|capture| *capture.borrow_mut() = ArtifactCapture::default());
    let store = ResourceStore::new(1, artifact_callbacks(), theme());
    let mut config = configuration(Some(1));
    config.batch_size = 1;
    let engines: Vec<_> = ["first", "second", "unrelated"]
        .into_iter()
        .map(|name| {
            let engine = store.list_resource(&ResourceKey::new(1, shared(name)), &config, 1);
            {
                let mut resource = engine.borrow_mut();
                for start in 0..3 {
                    resource.load_batch(start).unwrap();
                    // Artifact identity is scoped by source, even when IDs overlap.
                    resource
                        .batches
                        .get_mut(&start)
                        .unwrap()
                        .lease
                        .as_mut()
                        .unwrap()
                        .artifact_id = start as u64 + 1;
                }
                resource.last_batch = Some(1);
            }
            engine
        })
        .collect();
    let sources: Vec<_> = engines
        .iter()
        .map(|engine| engine.borrow().source_id)
        .collect();
    let mut keys = [
        NativeArtifactKey {
            source: sources[1],
            artifact: 2,
        },
        NativeArtifactKey {
            source: u64::MAX,
            artifact: 1,
        },
        NativeArtifactKey {
            source: sources[0],
            artifact: 3,
        },
        NativeArtifactKey {
            source: sources[0],
            artifact: 1,
        },
        NativeArtifactKey {
            source: sources[1],
            artifact: 2,
        },
        NativeArtifactKey {
            source: sources[0],
            artifact: u64::MAX,
        },
    ];
    assert!(!store.invalidate_artifacts(&mut []));
    assert!(store.invalidate_artifacts(&mut keys));
    for (index, expected) in [vec![1], vec![0, 2], vec![0, 1, 2]].iter().enumerate() {
        let resource = engines[index].borrow();
        assert_eq!(&batch_keys(&resource), expected);
        assert_eq!(
            resource.telemetry.batch_invalidations,
            (3 - expected.len()) as u64
        );
        assert_eq!(resource.last_batch, if index == 2 { Some(1) } else { None });
        assert_eq!(resource.state.max_offset_for_scrollbar().y, px(4_000.));
    }
    assert!(!store.invalidate_artifacts(&mut keys));
    ARTIFACTS.with(|capture| {
        let mut releases = capture.borrow().releases.clone();
        releases.sort_unstable();
        assert_eq!(
            releases,
            vec![(sources[0], 1, 0), (sources[0], 3, 0), (sources[1], 2, 0)]
        );
    });
}

#[test]
fn native_cache_eviction_and_invalidation_release_only_their_artifacts() {
    ARTIFACTS.with(|capture| *capture.borrow_mut() = ArtifactCapture::default());
    let mut resource = artifact_resource();
    for start in 0..5 {
        resource.use_clock += 1;
        resource.load_batch(start).unwrap();
        resource.begin_frame();
        resource.trim_batches();
    }
    ARTIFACTS.with(|capture| {
        let capture = capture.borrow();
        assert_eq!(capture.requests.len(), 5);
        assert_eq!(capture.releases, vec![(resource.source_id, 1, 0)]);
    });
    resource.invalidate_batches_intersecting(2, 1);
    ARTIFACTS.with(|capture| {
        assert_eq!(
            capture.borrow().releases,
            vec![(resource.source_id, 1, 0), (resource.source_id, 3, 0)]
        );
    });
    resource.clear_batches();
    drop(resource);
    ARTIFACTS.with(|capture| {
        let capture = capture.borrow();
        assert_eq!(capture.releases.len(), 5);
        assert_eq!(
            capture
                .releases
                .iter()
                .map(|(_, id, _)| *id)
                .collect::<HashSet<_>>()
                .len(),
            5
        );
    });
}

#[test]
fn sources_using_one_renderer_release_independently() {
    ARTIFACTS.with(|capture| *capture.borrow_mut() = ArtifactCapture::default());
    let mut first = artifact_resource();
    let mut second = artifact_resource();
    assert_eq!(first.renderer_token, second.renderer_token);
    assert_ne!(first.source_id, second.source_id);
    first.load_batch(0).unwrap();
    second.load_batch(0).unwrap();
    let first_source = first.source_id;
    drop(first);
    ARTIFACTS.with(|capture| assert_eq!(capture.borrow().releases, vec![(first_source, 1, 0)]));
    assert_eq!(second.batches.len(), 1);
    drop(second);
    ARTIFACTS.with(|capture| assert_eq!(capture.borrow().releases.len(), 2));
}

#[test]
fn declaration_removal_releases_artifacts_even_when_a_frame_retains_the_engine() {
    ARTIFACTS.with(|capture| *capture.borrow_mut() = ArtifactCapture::default());
    let store = ResourceStore::new(1, artifact_callbacks(), theme());
    let key = ResourceKey::new(1, "list".into());
    let engine = store.list_resource(&key, &configuration(Some(1)), 1);
    engine.borrow_mut().load_batch(0).unwrap();
    store.retain_snapshot(&ValidatedSnapshot::default());
    assert!(engine.borrow().batches.is_empty());
    ARTIFACTS.with(|capture| assert_eq!(capture.borrow().releases.len(), 1));
    drop(engine);
    ARTIFACTS.with(|capture| assert_eq!(capture.borrow().releases.len(), 1));
}

#[test]
fn failed_native_decode_releases_publication_after_the_borrow_ends() {
    for mode in 1..=3 {
        ARTIFACTS.with(|capture| {
            *capture.borrow_mut() = ArtifactCapture {
                failure_mode: mode,
                ..Default::default()
            }
        });
        let mut resource = artifact_resource();
        assert!(resource.load_batch(0).is_err());
        assert!(resource.batches.is_empty());
        ARTIFACTS.with(|capture| {
            let capture = capture.borrow();
            if mode == 3 {
                assert!(capture.releases.is_empty());
            } else {
                assert_eq!(capture.releases.len(), 1);
                assert_ne!(capture.releases[0].2, 0);
            }
        });
    }
}

fn theme() -> SharedTheme {
    Rc::new(RefCell::new(crate::theme::NativeTheme::default()))
}

fn callbacks() -> ManagedCallbacks {
    ManagedCallbacks {
        struct_size: 0,
        render: None,
        render_completed: None,
        release_artifact: None,
        accept_artifact: None,
        click: None,
        list_render_range: None,
        dynamic_frame: None,
        control_event: None,
        application_started: None,
        window_closed: None,
        menu_action: None,
        menu_applied: None,
        window_placement: None,
    }
}

fn configuration(content_revision: Option<u64>) -> ListConfiguration {
    ListConfiguration {
        tooltip_token: 0,
        tooltip: Default::default(),
        item_count: 100,
        renderer_token: 1,
        activation_token: 0,
        selection_token: 0,
        batch_size: 48,
        overdraw: px(240.),
        alignment: ListAlignment::Top,
        estimated_item_extent: px(40.),
        orientation: ListOrientation::Vertical,
        content_revision,
        scrollbar: ScrollbarMetrics::new(DEFAULT_SCROLLBAR_WIDTH, false),
        projection_revision: None,
    }
}

#[test]
fn estimated_height_hints_cover_the_full_unmeasured_range() {
    let mut config = configuration(Some(1));
    config.item_count = 20_000;
    let resource = CollectionEngine::new(1, callbacks(), &config, 1);

    assert_eq!(resource.state.max_offset_for_scrollbar().y, px(800_000.));
}

#[test]
fn estimated_height_hints_drive_native_pixel_offset_mapping() {
    let resource = CollectionEngine::new(1, callbacks(), &configuration(Some(1)), 1);

    resource.state.scroll_to(ListOffset {
        item_ix: 50,
        offset_in_item: px(20.),
    });

    assert_eq!(
        resource.state.scroll_px_offset_for_scrollbar().y,
        px(-2_020.)
    );
}

#[test]
fn structural_insertions_receive_estimated_height_hints() {
    let mut resource = CollectionEngine::new(1, callbacks(), &configuration(Some(1)), 1);
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 50, 5, ""));
    resource.commit_pending_commands(105);

    assert_eq!(resource.state.max_offset_for_scrollbar().y, px(4_200.));
}

#[test]
fn offscreen_refresh_preserves_full_scrollbar_range_after_configuration() {
    let mut config = configuration(Some(1));
    config.item_count = 20_000;
    let mut resource = CollectionEngine::new(1, callbacks(), &config, 1);
    resource.apply_command(&command(COMMAND_LIST_REFRESH, 10_000, 1_000, ""));
    resource.configure(&config, 2);
    assert_eq!(resource.state.max_offset_for_scrollbar().y, px(800_000.));
    resource.scroll_to_item(19_000);
    assert_eq!(
        resource.state.scroll_px_offset_for_scrollbar().y,
        px(-760_000.)
    );
}

#[test]
fn mixed_changes_wait_for_acceptance_and_reconcile_with_content_revision() {
    for changed_revision in [false, true] {
        let mut config = configuration(Some(1));
        config.item_count = 200;
        let mut resource = CollectionEngine::new(1, callbacks(), &config, 1);
        for start in [0, 48, 96, 144] {
            resource.batches.insert(start, CachedBatch::new());
        }
        resource.apply_command(&command(COMMAND_LIST_REFRESH, 50, 2, ""));
        resource.apply_command(&command(COMMAND_LIST_SPLICE, 150, (3_u64 << 32) | 8, ""));
        resource.apply_command(&command(COMMAND_LIST_REFRESH, 201, 4, ""));
        resource.configure(&config, 1);
        assert_eq!(resource.item_count, 200);
        assert_eq!(batch_keys(&resource), vec![0, 48, 96, 144]);
        config.item_count = 205;
        if changed_revision {
            config.content_revision = Some(2);
        }
        resource.configure(&config, 2);
        assert_eq!(resource.item_count, 205);
        assert_eq!(resource.state.max_offset_for_scrollbar().y, px(8_200.));
        assert!(resource.pending_commands.is_empty());
        assert_eq!(
            batch_keys(&resource),
            if changed_revision {
                vec![]
            } else {
                vec![0, 96]
            }
        );
    }
}

#[test]
fn invalid_mixed_hints_reset_to_the_accepted_shape() {
    let mut config = configuration(Some(1));
    let mut resource = CollectionEngine::new(1, callbacks(), &config, 1);
    resource.batches.insert(0, CachedBatch::new());
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 90, (11_u64 << 32) | 1, ""));
    resource.apply_command(&command(COMMAND_LIST_REFRESH, 0, 1, ""));
    config.item_count = 90;
    resource.configure(&config, 2);
    assert_eq!(resource.item_count, 90);
    assert_eq!(resource.state.max_offset_for_scrollbar().y, px(3_600.));
    assert!(resource.batches.is_empty());
    assert!(resource.pending_commands.is_empty());
}

#[test]
fn trimming_keeps_every_displayed_batch_and_the_four_newest_idle_batches() {
    for tied in [false, true] {
        let mut resource = resource_with_batches(&[]);
        resource.frame_start = 100;
        for key in 0..40 {
            let mut batch = CachedBatch::new();
            batch.last_used = if key >= 32 {
                101
            } else if tied {
                1
            } else {
                key as u64
            };
            resource.batches.insert(key, batch);
        }
        resource.trim_batches();
        assert_eq!(resource.batches.len(), 12);
        assert!((32..40).all(|key| resource.batches.contains_key(&key)));
        if !tied {
            assert_eq!(batch_keys(&resource), (28..40).collect::<Vec<_>>());
        }
        assert_eq!(resource.telemetry.batch_evictions, 28);
        resource.trim_batches();
        assert_eq!(resource.telemetry.batch_evictions, 28);
    }
}

#[test]
fn changing_estimated_extent_rebuilds_native_height_hints() {
    let mut resource = CollectionEngine::new(1, callbacks(), &configuration(Some(1)), 1);
    let mut changed = configuration(Some(1));
    changed.estimated_item_extent = px(52.);

    resource.configure(&changed, 2);

    assert_eq!(resource.state.max_offset_for_scrollbar().y, px(5_200.));
}

#[test]
fn explicit_content_revision_preserves_batches_across_unrelated_snapshots() {
    let revision = (1_u64 << 40) | 7;
    let mut resource = CollectionEngine::new(1, callbacks(), &configuration(Some(revision)), 1);
    resource.batches.insert(0, CachedBatch::new());

    resource.configure(&configuration(Some(revision)), 2);
    assert_eq!(resource.batches.len(), 1);

    resource.configure(&configuration(Some(revision + 1)), 3);
    assert!(resource.batches.is_empty());
}

#[test]
fn projection_changes_reset_same_count_rows_and_override_queued_hints() {
    for (previous, current) in [(None, Some(0)), (Some(0), Some(u64::MAX)), (Some(7), None)] {
        let mut config = configuration(Some(1));
        config.projection_revision = previous;
        let mut resource = CollectionEngine::new(1, callbacks(), &config, 1);
        resource.cursor.set(50);
        let old_epoch = resource.cursor.epoch();
        resource.state.scroll_to(ListOffset {
            item_ix: 50,
            offset_in_item: px(0.),
        });
        resource.batches.insert(0, CachedBatch::new());
        resource.apply_command(&command(COMMAND_LIST_SPLICE, 0, (1_u64 << 32) | 1, ""));

        config.projection_revision = current;
        resource.configure(&config, 2);

        assert_eq!(resource.cursor.active(), Some(0));
        assert!(!resource.cursor.set_from_item(50, old_epoch));
        assert_eq!(resource.state.scroll_px_offset_for_scrollbar().y, px(0.));
        assert!(resource.batches.is_empty());
        assert!(resource.pending_commands.is_empty());
        assert_eq!(resource.item_count, 100);
    }
}

#[test]
fn stable_projection_preserves_cursor_for_content_edits_and_valid_splices() {
    let mut config = configuration(Some(1));
    config.projection_revision = Some(0);
    let mut resource = CollectionEngine::new(1, callbacks(), &config, 1);
    resource.cursor.set(50);
    resource.batches.insert(0, CachedBatch::new());
    resource.configure(&config, 2);
    assert_eq!(resource.batches.len(), 1);
    assert_eq!(resource.cursor.active(), Some(50));

    config.content_revision = Some(2);
    resource.configure(&config, 3);
    assert!(resource.batches.is_empty());
    assert_eq!(resource.cursor.active(), Some(50));

    resource.apply_command(&command(COMMAND_LIST_SPLICE, 0, 2, ""));
    config.item_count += 2;
    resource.configure(&config, 4);
    assert_eq!(resource.cursor.active(), Some(52));
}

#[test]
fn implicit_content_revision_remains_conservative() {
    let mut resource = CollectionEngine::new(1, callbacks(), &configuration(None), 1);
    resource.batches.insert(0, CachedBatch::new());

    resource.configure(&configuration(None), 2);
    assert!(resource.batches.is_empty());
}

fn command(command: u16, a: u64, b: u64, data: &str) -> ResourceCommand {
    ResourceCommand {
        key: ResourceKey::new(7, shared("list")),
        resource_kind: RESOURCE_LIST,
        command,
        a,
        b,
        data: shared(data),
    }
}

fn resource_with_batches(keys: &[u32]) -> CollectionEngine {
    let mut resource = CollectionEngine::new(1, callbacks(), &configuration(None), 1);
    for &key in keys {
        resource.batches.insert(key, CachedBatch::new());
    }
    resource
}

fn batch_keys(resource: &CollectionEngine) -> Vec<u32> {
    let mut keys: Vec<_> = resource.batches.keys().copied().collect();
    keys.sort_unstable();
    keys
}

#[test]
fn refresh_invalidates_only_intersecting_batches() {
    let mut resource = resource_with_batches(&[0, 48, 96, 144]);
    resource.apply_command(&command(COMMAND_LIST_REFRESH, 50, 1, ""));
    resource.commit_pending_commands(100);

    assert_eq!(batch_keys(&resource), vec![0, 96, 144]);
    let telemetry = resource.telemetry();
    assert_eq!(telemetry.batch_invalidations, 1);
    assert_eq!(telemetry.full_invalidations, 0);
}

#[test]
fn collection_cursor_tracks_surviving_items_through_committed_splices() {
    let mut resource = resource_with_batches(&[0, 48, 96]);
    let cursor = resource.cursor.clone();
    cursor.set(70);
    let old_epoch = cursor.epoch();
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 10, 5, ""));
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 20, 3_u64 << 32, ""));
    assert_eq!(cursor.active(), Some(70)); // Hints have not been accepted yet.
    assert!(cursor.set_from_item(70, old_epoch));
    resource.commit_pending_commands(102);
    assert_eq!(cursor.active(), Some(72));
    assert!(!cursor.set_from_item(70, old_epoch));
    assert_eq!(cursor.active(), Some(72));
    assert!(cursor.set_from_item(80, cursor.epoch()));

    // Insertion exactly at the active position moves the existing item after the new items.
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 80, 2, ""));
    resource.commit_pending_commands(104);
    assert_eq!(cursor.active(), Some(82));
    // Cache eviction and stable-range refresh do not own the keyboard position.
    resource.clear_batches();
    resource.apply_command(&command(COMMAND_LIST_REFRESH, 80, 5, ""));
    resource.commit_pending_commands(104);
    assert_eq!(cursor.active(), Some(82));
}

#[test]
fn collection_cursor_chooses_successor_after_removal_and_clears_for_empty_lists() {
    let mut resource = resource_with_batches(&[]);
    let cursor = resource.cursor.clone();
    cursor.set(50);
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 48, (5_u64 << 32) | 2, ""));
    resource.commit_pending_commands(97);
    assert_eq!(cursor.active(), Some(48));
    cursor.set(96);
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 95, 2_u64 << 32, ""));
    resource.commit_pending_commands(95);
    assert_eq!(cursor.active(), Some(94));
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 0, 95_u64 << 32, ""));
    resource.commit_pending_commands(0);
    assert_eq!(cursor.active(), None);
    assert!(!cursor.set(0));
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 0, 3, ""));
    resource.commit_pending_commands(3);
    assert_eq!(cursor.active(), Some(0));
}

#[test]
fn collection_cursor_resets_when_structural_identity_is_unknown() {
    for reset_kind in ["explicit", "mismatch", "invalid", "declarative"] {
        let mut resource = resource_with_batches(&[]);
        resource.cursor.set(70);
        let old_epoch = resource.cursor.epoch();
        match reset_kind {
            "explicit" => {
                resource.apply_command(&command(COMMAND_LIST_RESET, 100, 0, ""));
                resource.commit_pending_commands(100);
            }
            "mismatch" => {
                resource.apply_command(&command(COMMAND_LIST_SPLICE, 0, 1, ""));
                resource.commit_pending_commands(100);
            }
            "invalid" => {
                resource.apply_command(&command(COMMAND_LIST_SPLICE, 101, 1, ""));
                resource.commit_pending_commands(100);
            }
            _ => {
                let mut config = configuration(Some(1));
                config.item_count = 99;
                resource.configure(&config, 2);
            }
        }
        assert_eq!(resource.cursor.active(), Some(0), "{reset_kind}");
        assert!(!resource.cursor.set_from_item(70, old_epoch));
    }
}

#[test]
fn collection_cursor_survives_content_and_layout_changes_including_simultaneous_splices() {
    let mut config = configuration(Some(1));
    let mut resource = CollectionEngine::new(1, callbacks(), &config, 1);
    let cursor = resource.cursor.clone();
    cursor.set(70);
    config.content_revision = Some(2);
    resource.configure(&config, 2);
    assert_eq!(cursor.active(), Some(70));
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 5, 3, ""));
    config.item_count = 103;
    config.estimated_item_extent = px(60.);
    resource.configure(&config, 3);
    assert_eq!(cursor.active(), Some(73));
    assert!(Rc::ptr_eq(&cursor, &resource.cursor));
    config.batch_size = 32;
    resource.configure(&config, 4);
    assert_eq!(cursor.active(), Some(73));
    // A removed/recreated native resource starts a new independent cursor.
    let recreated = CollectionEngine::new(1, callbacks(), &config, 4);
    assert_eq!(recreated.cursor.active(), Some(0));
    assert!(!Rc::ptr_eq(&cursor, &recreated.cursor));
}

#[test]
fn refresh_spanning_multiple_batches_invalidates_each_one() {
    let mut resource = resource_with_batches(&[0, 48, 96, 144]);
    resource.apply_command(&command(COMMAND_LIST_REFRESH, 40, 20, ""));
    resource.commit_pending_commands(100);

    // [40, 60) touches batch 0 ([0, 48)) and batch 48 ([48, 96)).
    assert_eq!(batch_keys(&resource), vec![96, 144]);
    assert_eq!(resource.telemetry().batch_invalidations, 2);
}

#[test]
fn splice_preserves_batches_entirely_before_start() {
    let mut resource = resource_with_batches(&[0, 48, 96, 144]);
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 90, (10_u64 << 32) | 10, ""));
    resource.commit_pending_commands(100);

    // The suffix at or after batch 48 contains index 90 and shifts; batch 0 is untouched.
    assert_eq!(batch_keys(&resource), vec![0]);
    assert_eq!(resource.telemetry().batch_invalidations, 3);
}

#[test]
fn splice_at_head_invalidates_every_cached_batch() {
    let mut resource = resource_with_batches(&[0, 48, 96]);
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 0, (5_u64 << 32) | 2, ""));
    resource.commit_pending_commands(97);

    assert!(resource.batches.is_empty());
    assert_eq!(resource.telemetry().batch_invalidations, 3);
}

#[test]
fn splice_suffix_start_snaps_to_batch_boundary() {
    let mut resource = resource_with_batches(&[0, 48, 96]);
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 60, (1_u64 << 32) | 1, ""));
    resource.commit_pending_commands(100);

    // Batch 48 covers [48, 96), which contains index 60, so it is stale; batch 0 survives.
    assert_eq!(batch_keys(&resource), vec![0]);
    assert_eq!(resource.telemetry().batch_invalidations, 2);
}

#[test]
fn reset_command_clears_every_batch() {
    let mut resource = resource_with_batches(&[0, 48, 96]);
    resource.apply_command(&command(12, 50, 0, ""));
    resource.commit_pending_commands(50);

    assert!(resource.batches.is_empty());
    let telemetry = resource.telemetry();
    assert_eq!(telemetry.full_invalidations, 1);
    assert_eq!(telemetry.batch_invalidations, 0);
}

#[test]
fn multi_range_refresh_invalidates_exactly_the_intersecting_batches() {
    let mut resource = resource_with_batches(&[0, 48, 96, 144]);
    resource.item_count = 200;
    resource.apply_command(&command(COMMAND_LIST_REFRESH, 50, 1, ""));
    resource.apply_command(&command(COMMAND_LIST_REFRESH, 150, 3, ""));
    resource.commit_pending_commands(200);

    assert_eq!(batch_keys(&resource), vec![0, 96]);
    assert_eq!(resource.telemetry().batch_invalidations, 2);
}

#[test]
fn multi_range_refresh_after_splice_uses_post_splice_indices() {
    let mut resource = resource_with_batches(&[0, 48, 96, 144]);
    resource.apply_command(&command(COMMAND_LIST_SPLICE, 40, (2_u64 << 32) | 2, ""));
    resource.apply_command(&command(COMMAND_LIST_REFRESH, 38, 1, ""));
    resource.commit_pending_commands(100);

    // The splice at 40 has batch boundary 0, so every cached batch is already stale; the
    // following refresh finds nothing left to invalidate and must not underflow or panic.
    assert!(resource.batches.is_empty());
    assert_eq!(resource.telemetry().batch_invalidations, 4);
}

#[test]
fn hints_disagreeing_with_declared_count_fall_back_to_full_reset() {
    let mut resource = resource_with_batches(&[0, 48, 96]);
    resource.apply_command(&command(COMMAND_LIST_REFRESH, 10, 5, ""));
    resource.commit_pending_commands(101);

    assert!(resource.batches.is_empty());
    assert_eq!(resource.item_count, 101);
    assert_eq!(resource.telemetry().full_invalidations, 1);
}

#[test]
fn refresh_zero_count_is_a_no_op() {
    let mut resource = resource_with_batches(&[0, 48]);
    resource.apply_command(&command(13, 10, 0, ""));
    resource.commit_pending_commands(100);

    assert_eq!(batch_keys(&resource), vec![0, 48]);
    let telemetry = resource.telemetry();
    assert_eq!(telemetry.batch_invalidations, 0);
    assert_eq!(telemetry.full_invalidations, 0);
}

#[test]
fn refresh_zero_count_after_real_range_preserves_earlier_invalidation() {
    let mut resource = resource_with_batches(&[0, 48, 96]);
    resource.apply_command(&command(COMMAND_LIST_REFRESH, 50, 1, ""));
    resource.apply_command(&command(13, 10, 0, ""));
    resource.commit_pending_commands(100);

    assert_eq!(batch_keys(&resource), vec![0, 96]);
    assert_eq!(resource.telemetry().batch_invalidations, 1);
}

#[test]
fn parse_table_spec_reads_string_pairs_and_packed_records() {
    let data = "rows\0name\0Name\0size\0Size";
    let records = [
        pack_table_column(120.0f32.to_bits(), 0, 0),
        pack_table_column(0.3f32.to_bits(), 1, 2),
    ];
    let (key, columns) = parse_table_spec(data, &records).expect("valid table data");
    assert_eq!(key, shared("rows"));
    assert_eq!(columns.len(), 2);
    assert_eq!(columns[0].key, shared("name"));
    assert_eq!(columns[0].header, shared("Name"));
    assert_eq!(columns[0].width, px(120.));
    assert!(!columns[0].width_is_fraction);
    assert_eq!(columns[0].alignment, 0);
    assert_eq!(columns[1].width, px(0.3));
    assert!(columns[1].width_is_fraction);
    assert_eq!(columns[1].alignment, 2);

    // A key-only blob is a headerless table with no columns.
    let (key, columns) = parse_table_spec("rows", &[]).expect("valid key-only table data");
    assert_eq!(key, shared("rows"));
    assert!(columns.is_empty());
}

#[test]
fn parse_table_spec_rejects_malformed_specs() {
    let good = pack_table_column(120.0f32.to_bits(), 0, 0);
    let bad_unit = pack_table_column(120.0f32.to_bits(), 2, 0);
    let bad_alignment = pack_table_column(120.0f32.to_bits(), 0, 3);
    let bad_width = pack_table_column((-4.0f32).to_bits(), 0, 0);
    let bad_fraction = pack_table_column(1.4f32.to_bits(), 1, 0);
    let noncanonical = pack_table_column(120.0f32.to_bits(), 0, 0) | (1 << 36);

    // String count must be exactly two per record.
    assert_eq!(parse_table_spec("", &[good]), None);
    assert_eq!(parse_table_spec("\0name\0Name", &[good]), None);
    assert_eq!(parse_table_spec("rows\0name\0Name\0extra", &[good]), None);
    assert_eq!(parse_table_spec("rows\0name\0Name\0x", &[good, good]), None);
    assert_eq!(parse_table_spec("rows\0name\0Name\0x\0y\0z", &[good]), None);
    // An empty column key is rejected.
    assert_eq!(parse_table_spec("rows\0\0Name", &[good]), None);
    // Numeric records must unpack cleanly.
    assert_eq!(parse_table_spec("rows\0name\0Name", &[bad_unit]), None);
    assert_eq!(parse_table_spec("rows\0name\0Name", &[bad_alignment]), None);
    assert_eq!(parse_table_spec("rows\0name\0Name", &[bad_width]), None);
    assert_eq!(parse_table_spec("rows\0name\0Name", &[bad_fraction]), None);
    assert_eq!(parse_table_spec("rows\0name\0Name", &[noncanonical]), None);
}

#[test]
fn unpack_table_column_round_trips_packed_records() {
    for (width, unit, alignment) in [
        (8.0f32, 0u32, 0u32),
        (120.0, 0, 2),
        (0.5, 1, 1),
        (1.0, 1, 2),
    ] {
        let record = pack_table_column(width.to_bits(), unit, alignment);
        let unpacked = unpack_table_column(record).expect("record round-trips");
        assert_eq!(unpacked.0, width);
        assert_eq!(unpacked.1, unit == 1);
        assert_eq!(unpacked.2, alignment);
    }
}

#[test]
fn binding_a_changed_table_spec_invalidates_row_batches() {
    let store = ResourceStore::new(1, callbacks(), theme());
    let configuration = configuration(Some(1));
    let engine = store.list_resource(&ResourceKey::new(7, shared("rows")), &configuration, 1);

    let spec = TableSpec {
        columns: vec![TableColumnSpec {
            key: shared("name"),
            header: shared("Name"),
            width: px(120.),
            width_is_fraction: false,
            alignment: 0,
        }],
    };
    store.bind_table_spec(
        &ResourceKey::new(7, shared("rows")),
        Rc::new(spec.clone()),
        &engine,
    );
    engine.borrow_mut().batches.insert(0, CachedBatch::new());
    assert_eq!(engine.borrow().batches.len(), 1);

    // Rebinding the identical spec is a no-op.
    store.bind_table_spec(
        &ResourceKey::new(7, shared("rows")),
        Rc::new(spec.clone()),
        &engine,
    );
    assert_eq!(engine.borrow().batches.len(), 1);

    engine.borrow_mut().batches.insert(48, CachedBatch::new());
    engine
        .borrow_mut()
        .apply_command(&command(COMMAND_LIST_REFRESH, 50, 1, ""));
    engine
        .borrow_mut()
        .apply_command(&command(COMMAND_LIST_SPLICE, 90, (1_u64 << 32) | 1, ""));
    let retained = store.list_resource(&ResourceKey::new(7, shared("rows")), &configuration, 2);
    assert!(Rc::ptr_eq(&engine, &retained));
    assert_eq!(batch_keys(&engine.borrow()), vec![0]);

    // Column changes still invalidate items preserved by targeted refresh/splice hints.
    let changed = TableSpec {
        columns: vec![
            spec.columns[0].clone(),
            TableColumnSpec {
                key: shared("size"),
                header: shared("Size"),
                width: px(80.),
                width_is_fraction: false,
                alignment: 2,
            },
        ],
    };
    store.bind_table_spec(
        &ResourceKey::new(7, shared("rows")),
        Rc::new(changed),
        &engine,
    );
    assert!(engine.borrow().batches.is_empty());
    assert_eq!(
        store
            .table_spec(&ResourceKey::new(7, shared("rows")))
            .unwrap()
            .columns
            .len(),
        2
    );
}

#[test]
fn ambient_render_change_invalidates_retained_list_and_table_rows() {
    let store = ResourceStore::new(1, callbacks(), theme());
    let list = store.list_resource(
        &ResourceKey::new(7, shared("list")),
        &configuration(Some(1)),
        1,
    );
    let table = store.list_resource(
        &ResourceKey::new(7, shared("table")),
        &configuration(Some(1)),
        1,
    );
    list.borrow_mut().batches.insert(0, CachedBatch::new());
    table.borrow_mut().batches.insert(0, CachedBatch::new());

    store.invalidate_managed_rendered_items();

    assert!(list.borrow().batches.is_empty());
    assert!(table.borrow().batches.is_empty());
    assert_eq!(list.borrow().telemetry().full_invalidations, 1);
    assert_eq!(table.borrow().telemetry().full_invalidations, 1);
}

#[test]
fn click_refresh_flow_preserves_scroll_across_configure_and_rebind() {
    let key = ResourceKey::new(7, shared("grid"));
    let store = ResourceStore::new(1, callbacks(), theme());
    let mut config = configuration(None);
    config.item_count = 5_000;
    config.batch_size = 64;

    // First materialization: the table declares columns and the collection engine is created.
    let engine = store.list_resource(&key, &config, 1);
    let spec = TableSpec {
        columns: vec![TableColumnSpec {
            key: shared("name"),
            header: shared("Name"),
            width: px(120.),
            width_is_fraction: false,
            alignment: 0,
        }],
    };
    store.bind_table_spec(&key, Rc::new(spec), &engine);

    // The user scrolls down to item 1000.
    engine.borrow().state.scroll_to(ListOffset {
        item_ix: 1000,
        offset_in_item: px(0.),
    });
    assert_eq!(engine.borrow().state.logical_scroll_top().item_ix, 1000);

    // Click: RefreshRanges queues two Refresh commands, then invalidates the view.
    engine.borrow_mut().apply_command(&command(13, 40, 1, ""));
    engine.borrow_mut().apply_command(&command(13, 41, 1, ""));

    // Next frame: the snapshot revision advanced and the table re-materialized.
    let engine = store.list_resource(&key, &config, 2);
    let spec = TableSpec {
        columns: vec![TableColumnSpec {
            key: shared("name"),
            header: shared("Name"),
            width: px(120.),
            width_is_fraction: false,
            alignment: 0,
        }],
    };
    store.bind_table_spec(&key, Rc::new(spec), &engine);

    let telemetry = engine.borrow().telemetry();
    assert_eq!(telemetry.full_invalidations, 0);
    assert_eq!(engine.borrow().state.logical_scroll_top().item_ix, 1000);
}

/// Regression test: a managed re-render must not drop a table's collection engine. Table nodes
/// were skipped by the retain adapter guard, so the first snapshot commit after a table
/// appeared evicted the engine (fresh ListState = scroll jumped to the top).
#[test]
fn retain_snapshot_keeps_table_row_engines_across_renders() {
    use crate::{
        abi::{NodeRecord, OpRecord, RenderArena},
        semantic::{COMPONENT_TABLE, ValueKind},
    };

    let data = b"grid\0name\x1FName\x1F120\x1F0\x1F0";
    let mut node = NodeRecord {
        component: COMPONENT_TABLE,
        data_offset: 0,
        data_length: data.len() as u32,
        ..Default::default()
    };
    let mut operation = OpRecord {
        node: 0,
        code: OP_RESOURCE_OWNER,
        value_kind: ValueKind::U32 as u16,
        a: 4,
        ..Default::default()
    };
    let mut utf8 = data.to_vec();
    let arena = RenderArena {
        nodes: &mut node,
        node_length: 1,
        node_capacity: 1,
        ops: &mut operation,
        op_length: 1,
        op_capacity: 1,
        children: std::ptr::null_mut(),
        child_length: 0,
        child_capacity: 0,
        utf8: utf8.as_mut_ptr(),
        utf8_length: utf8.len() as i32,
        utf8_capacity: utf8.len() as i32,
        generation: 1,
        flags: 0,
        required_node_capacity: 0,
        required_op_capacity: 0,
        required_child_capacity: 0,
        required_utf8_capacity: 0,
    };
    let mut snapshot = ValidatedSnapshot::default();
    let mut retained_strings = RetainedStrings::default();
    let mut scratch = SnapshotScratch::default();
    snapshot
        .decode_into(&arena, 0, &mut retained_strings, &mut scratch)
        .expect("table snapshot decodes");

    let key = ResourceKey::new(4, shared("grid"));
    let store = ResourceStore::new(1, callbacks(), theme());
    let engine = store.list_resource(&key, &configuration(None), 1);
    store.retain_snapshot(&snapshot);
    assert!(
        store
            .list_engines()
            .iter()
            .any(|retained| Rc::ptr_eq(retained, &engine)),
        "engine retained"
    );

    // A later re-render retains the same snapshot shape; the engine must survive again.
    store.retain_snapshot(&snapshot);
    assert_eq!(store.list_engine_count(), 1);
}
