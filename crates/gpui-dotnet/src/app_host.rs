use std::{
    cell::RefCell,
    collections::{HashMap, HashSet},
    rc::Rc,
    sync::{
        Arc, Mutex, OnceLock,
        atomic::{AtomicBool, AtomicI32, Ordering},
    },
    time::Duration,
};

use async_channel::{Receiver, Sender, TrySendError};
use gpui::{
    AnyWindowHandle, App, AppContext, Bounds, Context, IntoElement, Menu, MenuItem, Render,
    TitlebarOptions, Window, WindowBounds, WindowDecorations, WindowOptions, div, point,
    prelude::*, px, rgba, size,
};

use crate::{
    abi::ManagedCallbacks,
    arena::with_root_render_output,
    extension::NativeExtensionCommand,
    overlay::OverlayStack,
    popover_menu::PopoverMenuGroup,
    presence::ResourcePresence,
    resources::{ResourceCommand, ResourceStore},
    semantic::{COMPONENT_DYNAMIC, OP_DYNAMIC_ACTIVE, OP_RESOURCE_OWNER},
    snapshot::{RetainedStrings, SnapshotScratch, ValidatedSnapshot},
    theme::{NativeTheme, SharedTheme},
    trace,
    window_toast::{WindowToast, WindowToastHost},
};

const INGRESS_CAPACITY: usize = 4096;

pub(crate) struct ManagedView {
    pub(crate) view_id: u64,
    pub(crate) callbacks: ManagedCallbacks,
    retained_strings: RetainedStrings,
    pub(crate) snapshot: ValidatedSnapshot,
    snapshot_scratch: SnapshotScratch,
    has_snapshot: bool,
    pub(crate) snapshot_revision: u64,
    dirty: bool,
    pub(crate) error: Option<String>,
    pub(crate) resources: Rc<ResourceStore>,
    presence: Arc<Mutex<ResourcePresence>>,
    pub(crate) popover_menus: Rc<PopoverMenuGroup>,
    pub(crate) overlay_stack: Rc<OverlayStack>,
    pub(crate) theme: SharedTheme,
    toasts: WindowToastHost,
    toast_clock_running: bool,
}

enum ViewMessage {
    Invalidate,
    InvalidateArtifacts(Vec<crate::abi::NativeArtifactKey>),
    ResourceCommand(ResourceCommand, u64),
    ExtensionCommand(NativeExtensionCommand, u64),
}

#[derive(Clone)]
struct ViewNotifier {
    sender: Sender<ViewMessage>,
    invalidate_pending: Arc<AtomicBool>,
    presence: Arc<Mutex<ResourcePresence>>,
}

static VIEW_NOTIFIERS: OnceLock<Mutex<HashMap<u64, ViewNotifier>>> = OnceLock::new();

fn view_notifiers() -> &'static Mutex<HashMap<u64, ViewNotifier>> {
    VIEW_NOTIFIERS.get_or_init(|| Mutex::new(HashMap::new()))
}

struct ViewRegistration {
    view_id: u64,
}

impl ViewRegistration {
    fn new(view_id: u64, notifier: ViewNotifier) -> Result<Self, i32> {
        let mut notifiers = view_notifiers().lock().map_err(|_| -32)?;
        if notifiers.contains_key(&view_id) {
            return Err(-30);
        }
        notifiers.insert(view_id, notifier);
        Ok(Self { view_id })
    }
}

impl Drop for ViewRegistration {
    fn drop(&mut self) {
        if let Ok(mut notifiers) = view_notifiers().lock() {
            notifiers.remove(&self.view_id);
        }
    }
}

#[derive(Debug)]
pub(crate) enum ApplicationCommand {
    SetMenuBar {
        menus: Vec<ManagedMenu>,
        generation: u64,
    },
    SetTheme(NativeTheme),
    SetImageCacheBudget {
        max_bytes: u64,
        max_entries: u64,
    },
    EvictImage {
        path: String,
    },
    ManagedCodeUpdated,
    Open {
        window_id: u64,
        title: String,
        left: Option<f32>,
        top: Option<f32>,
        width: f32,
        height: f32,
        activate: bool,
        title_bar_style: WindowTitleBarStyle,
        initial_state: WindowInitialState,
    },
    Close(u64),
    Activate(u64),
    Minimize(u64),
    ToggleMaximize(u64),
    ToggleFullscreen(u64),
    ShowToast {
        window_id: u64,
        toast: WindowToast,
    },
    DismissToast {
        window_id: u64,
        id: String,
    },
    ClearToasts(u64),
    SetTitle {
        window_id: u64,
        title: String,
    },
    Resize {
        window_id: u64,
        width: f32,
        height: f32,
    },
}

#[derive(Debug)]
pub(crate) struct ManagedMenu {
    pub(crate) title: String,
    pub(crate) items: Vec<ManagedMenuItem>,
}

#[derive(Debug)]
pub(crate) enum ManagedMenuItem {
    Separator,
    Action { id: u64, title: String },
    Submenu(ManagedMenu),
}

#[derive(Clone, PartialEq, Debug, gpui::Action)]
#[action(no_json, no_register)]
struct ManagedMenuAction {
    id: u64,
    generation: u64,
}

#[derive(Default)]
struct MenuGeneration(u64);
impl gpui::Global for MenuGeneration {}

fn dispatch_menu_action(
    action: &ManagedMenuAction,
    application_id: u64,
    callbacks: ManagedCallbacks,
    cx: &App,
) -> i32 {
    if action.generation != cx.global::<MenuGeneration>().0 {
        return 0;
    }
    unsafe {
        callbacks
            .menu_action
            .expect("validated menu action callback")(application_id, action.id)
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub(crate) enum WindowTitleBarStyle {
    System,
    Custom,
    Hidden,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub(crate) enum WindowInitialState {
    Normal,
    Maximized,
    Fullscreen,
}

#[derive(Clone)]
struct ApplicationNotifier {
    sender: Sender<ApplicationCommand>,
}

static APPLICATION_NOTIFIERS: OnceLock<Mutex<HashMap<u64, ApplicationNotifier>>> = OnceLock::new();

fn application_notifiers() -> &'static Mutex<HashMap<u64, ApplicationNotifier>> {
    APPLICATION_NOTIFIERS.get_or_init(|| Mutex::new(HashMap::new()))
}

struct ApplicationRegistration {
    application_id: u64,
}

impl ApplicationRegistration {
    fn new(application_id: u64, notifier: ApplicationNotifier) -> Result<Self, i32> {
        let mut notifiers = application_notifiers().lock().map_err(|_| -42)?;
        if notifiers.contains_key(&application_id) {
            return Err(-40);
        }
        notifiers.insert(application_id, notifier);
        Ok(Self { application_id })
    }
}

impl Drop for ApplicationRegistration {
    fn drop(&mut self) {
        if let Ok(mut notifiers) = application_notifiers().lock() {
            notifiers.remove(&self.application_id);
        }
    }
}

struct ManagedWindowRegistration {
    handle: AnyWindowHandle,
    _view_registration: ViewRegistration,
}

type ManagedWindows = Rc<RefCell<HashMap<u64, ManagedWindowRegistration>>>;

pub(crate) fn notify(view_id: u64) -> i32 {
    let notifier = {
        let Ok(notifiers) = view_notifiers().lock() else {
            return -32;
        };
        let Some(notifier) = notifiers.get(&view_id) else {
            return -30;
        };
        notifier.clone()
    };

    if notifier.invalidate_pending.swap(true, Ordering::AcqRel) {
        return 0;
    }

    match notifier.sender.try_send(ViewMessage::Invalidate) {
        Ok(()) => 0,
        Err(TrySendError::Full(_)) => {
            notifier.invalidate_pending.store(false, Ordering::Release);
            -33
        }
        Err(TrySendError::Closed(_)) => {
            notifier.invalidate_pending.store(false, Ordering::Release);
            -31
        }
    }
}

pub(crate) fn dispatch_command(view_id: u64, command: ResourceCommand) -> i32 {
    let notifier = {
        let Ok(notifiers) = view_notifiers().lock() else {
            return -32;
        };
        let Some(notifier) = notifiers.get(&view_id) else {
            return -30;
        };
        notifier.clone()
    };
    let generation = {
        let Ok(presence) = notifier.presence.lock() else {
            return -32;
        };
        let Some(generation) = presence.base_generation(command.resource_kind, &command.key) else {
            return -34;
        };
        generation
    };
    match notifier
        .sender
        .try_send(ViewMessage::ResourceCommand(command, generation))
    {
        Ok(()) => 0,
        Err(TrySendError::Full(_)) => -33,
        Err(TrySendError::Closed(_)) => -31,
    }
}

pub(crate) fn dispatch_extension_command(view_id: u64, command: NativeExtensionCommand) -> i32 {
    let notifier = {
        let Ok(notifiers) = view_notifiers().lock() else {
            return -32;
        };
        let Some(notifier) = notifiers.get(&view_id) else {
            return -30;
        };
        notifier.clone()
    };
    let generation = {
        let Ok(presence) = notifier.presence.lock() else {
            return -32;
        };
        let Some(generation) = presence.extension_generation(&command.resource_key) else {
            return -34;
        };
        generation
    };
    match notifier
        .sender
        .try_send(ViewMessage::ExtensionCommand(command, generation))
    {
        Ok(()) => 0,
        Err(TrySendError::Full(_)) => -33,
        Err(TrySendError::Closed(_)) => -31,
    }
}

pub(crate) fn invalidate_artifacts(view_id: u64, keys: Vec<crate::abi::NativeArtifactKey>) -> i32 {
    let sender = {
        let Ok(notifiers) = view_notifiers().lock() else {
            return -32;
        };
        let Some(notifier) = notifiers.get(&view_id) else {
            return -30;
        };
        notifier.sender.clone()
    };
    match sender.try_send(ViewMessage::InvalidateArtifacts(keys)) {
        Ok(()) => 0,
        Err(TrySendError::Full(_)) => -33,
        Err(TrySendError::Closed(_)) => -31,
    }
}

pub(crate) fn dispatch_application_command(
    application_id: u64,
    command: ApplicationCommand,
) -> i32 {
    let sender = {
        let Ok(notifiers) = application_notifiers().lock() else {
            return -42;
        };
        let Some(notifier) = notifiers.get(&application_id) else {
            return -40;
        };
        notifier.sender.clone()
    };
    match sender.try_send(command) {
        Ok(()) => 0,
        Err(TrySendError::Full(_)) => -43,
        Err(TrySendError::Closed(_)) => -41,
    }
}

impl ManagedView {
    pub(crate) fn new(
        view_id: u64,
        callbacks: ManagedCallbacks,
        theme: SharedTheme,
        presence: Arc<Mutex<ResourcePresence>>,
    ) -> Self {
        Self {
            view_id,
            callbacks,
            retained_strings: RetainedStrings::default(),
            snapshot: ValidatedSnapshot::default(),
            snapshot_scratch: SnapshotScratch::default(),
            has_snapshot: false,
            snapshot_revision: 0,
            dirty: true,
            error: None,
            resources: Rc::new(ResourceStore::new(view_id, callbacks, theme.clone())),
            presence,
            popover_menus: Rc::new(PopoverMenuGroup::default()),
            overlay_stack: OverlayStack::new(),
            theme,
            toasts: WindowToastHost::new(),
            toast_clock_running: false,
        }
    }

    fn show_toast(&mut self, toast: WindowToast, window: &mut Window, cx: &mut Context<Self>) {
        self.toasts.push(toast, cx.background_executor().now());
        cx.notify();
        if self.toast_clock_running {
            return;
        }
        self.toast_clock_running = true;
        cx.spawn_in(window, async move |this, cx| {
            loop {
                cx.background_executor()
                    .timer(Duration::from_millis(50))
                    .await;
                let Ok(continue_clock) = this.update(cx, |view, cx| {
                    if view.toasts.advance(cx.background_executor().now()) {
                        cx.notify();
                    }
                    if view.toasts.is_empty() {
                        view.toast_clock_running = false;
                        false
                    } else {
                        true
                    }
                }) else {
                    break;
                };
                if !continue_clock {
                    break;
                }
            }
        })
        .detach();
    }

    pub(crate) fn dismiss_toast(&mut self, id: &str, cx: &mut Context<Self>) {
        if self.toasts.dismiss(id, cx.background_executor().now()) {
            cx.notify();
        }
    }

    fn clear_toasts(&mut self, cx: &mut Context<Self>) {
        if self.toasts.clear(cx.background_executor().now()) {
            cx.notify();
        }
    }

    fn deliver_resource_command(&self, command: ResourceCommand, generation: u64) -> bool {
        if self.error.is_some()
            || self
                .presence
                .lock()
                .ok()
                .and_then(|presence| presence.base_generation(command.resource_kind, &command.key))
                != Some(generation)
        {
            return false;
        }
        let notify_native_only = matches!(
            command.resource_kind,
            crate::semantic::RESOURCE_SCROLL | crate::semantic::RESOURCE_INPUT
        ) || (command.resource_kind == crate::semantic::RESOURCE_LIST
            && command.command == crate::semantic::COMMAND_LIST_SCROLL_TO_ITEM);
        self.resources.dispatch(command);
        notify_native_only
    }

    fn deliver_artifact_invalidations(&self, keys: &mut [crate::abi::NativeArtifactKey]) -> bool {
        self.error.is_none() && self.resources.invalidate_artifacts(keys)
    }

    fn deliver_extension_command(&self, command: NativeExtensionCommand, generation: u64) -> bool {
        if self.error.is_some()
            || self
                .presence
                .lock()
                .ok()
                .and_then(|presence| presence.extension_generation(&command.resource_key))
                != Some(generation)
        {
            return false;
        }
        self.resources.extensions().enqueue_command(command);
        true
    }

    fn refresh_if_dirty(&mut self) {
        if !self.dirty {
            return;
        }

        self.dirty = false;
        self.error = None;
        let render = self
            .callbacks
            .render
            .expect("callbacks were validated before application startup");
        let view_id = self.view_id;
        let complete = self
            .callbacks
            .render_completed
            .expect("callbacks were validated before application startup");
        let decode_result = with_root_render_output(
            |arena, root, revision| {
                let _stage = trace::span(trace::Stage::ManagedRender);
                unsafe { render(view_id, arena, root, revision) }
            },
            |arena, root, revision| {
                if revision <= self.snapshot_revision {
                    return Err(-40);
                }
                let _stage = trace::span(trace::Stage::SnapshotDecode);
                self.snapshot.decode_into(
                    arena,
                    root,
                    &mut self.retained_strings,
                    &mut self.snapshot_scratch,
                )?;
                self.snapshot_revision = revision;
                let _stage = trace::span(trace::Stage::Retain);
                self.resources.retain_snapshot(&self.snapshot);
                self.resources
                    .publish_presence(&mut *self.presence.lock().map_err(|_| -32)?, revision);
                Ok(())
            },
            |revision, status| unsafe { complete(view_id, revision, status) },
        );
        match decode_result {
            Ok(()) => {
                self.has_snapshot = true;
            }
            Err(status) => {
                self.error = Some(format!(
                    "Managed render or snapshot validation failed with status {status}."
                ));
            }
        }
    }

    fn schedule_dynamic_frame(
        &self,
        owners: Vec<u32>,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) {
        if owners.is_empty() {
            return;
        }

        let session_id = self.view_id;
        let callback = self
            .callbacks
            .dynamic_frame
            .expect("callbacks were validated before application startup");
        let view = cx.entity().downgrade();
        window.on_next_frame(move |_, cx| {
            let status = owners
                .iter()
                .map(|owner| unsafe { callback(session_id, *owner) })
                .find(|status| *status != 0)
                .unwrap_or(0);
            let _ = view.update(cx, |view, cx| {
                if status == 0 {
                    view.invalidate(cx);
                } else {
                    view.error = Some(format!(
                        "Managed dynamic-frame callback failed with status {status}."
                    ));
                    cx.notify();
                }
            });
        });
        window.request_animation_frame();
    }

    pub(crate) fn after_click(&mut self, status: i32, cx: &mut Context<Self>) {
        if status != 0 && self.error.is_none() {
            self.error = Some(format!(
                "Managed click callback failed with status {status}."
            ));
            self.invalidate(cx);
        }
    }

    fn invalidate(&mut self, cx: &mut Context<Self>) {
        self.dirty = true;
        cx.notify();
    }

    /// Cumulative list cache telemetry across every active list/table collection engine, appended to
    /// the per-frame trace report.
    fn list_telemetry_sums(&self) -> [(&'static str, u64); 8] {
        let engines = self.resources.list_engine_count();
        let mut sums = [0u64; 7];
        for engine in self.resources.list_engines() {
            let telemetry = engine.borrow().telemetry();
            sums[0] += telemetry.batch_loads;
            sums[1] += telemetry.batch_cache_hits;
            sums[2] += telemetry.batch_evictions;
            sums[3] += telemetry.batch_invalidations;
            sums[4] += telemetry.full_invalidations;
            sums[5] += telemetry.batch_crossings;
            sums[6] += telemetry.rendered_items;
        }
        [
            ("engines", engines as u64),
            ("loads", sums[0]),
            ("hits", sums[1]),
            ("evict", sums[2]),
            ("inval", sums[3]),
            ("full", sums[4]),
            ("cross", sums[5]),
            ("items", sums[6]),
        ]
    }
}

pub(crate) fn after_detached_callback(session_id: u64, status: i32) {
    if status != 0 {
        // Managed callback failure is terminal and preserves its exception. Wake the normal
        // refresh path to display that failure; successful row events need no host lookup.
        let _ = notify(session_id);
    }
}

impl Render for ManagedView {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        self.refresh_if_dirty();
        self.overlay_stack.begin_frame();
        self.resources.item_menus.begin_frame(window, cx);
        self.resources.item_tooltips.begin_frame();
        let theme = *self.theme.borrow();
        let dynamic_owners = if self.error.is_none() && self.has_snapshot {
            active_dynamic_owners(&self.snapshot)
        } else {
            Vec::new()
        };

        let content = {
            let _stage = trace::span(trace::Stage::Materialize);
            // Ensure the scoped image cache exists before materialization so every `img`
            // resolves through it instead of GPUI's never-evicted global asset map.
            let _image_cache = self.resources.image_cache(cx);
            if let Some(error) = &self.error {
                div()
                    .p(px(20.0))
                    .text_color(rgba(theme.error))
                    .child(error.clone())
                    .into_any_element()
            } else if self.has_snapshot {
                self.materialize_node(self.snapshot.root, &self.snapshot, window, cx)
            } else {
                div()
                    .child("No managed snapshot was published.")
                    .into_any_element()
            }
        };

        self.resources.item_menus.finish_declarations(window, cx);
        self.resources.item_tooltips.finish_declarations(window);

        if self.error.is_none() && self.has_snapshot {
            // The snapshot is authoritative: release decoded images (and their GPU textures)
            // that neither mounted nodes nor cached virtual-item batches declare anymore.
            // Revision gating keeps re-renders of an unchanged tree allocation-free.
            self.resources
                .retain_live_images(&self.snapshot, self.snapshot_revision, window, cx);
        }

        if trace::enabled() {
            trace::end_frame(&self.list_telemetry_sums());
        }
        self.schedule_dynamic_frame(dynamic_owners, window, cx);

        let item_tooltips = self.resources.item_tooltips.clone();
        let toast_layer = (!self.toasts.is_empty()).then(|| self.toasts.layer(theme, cx));
        // The managed root's Grow/Shrink styles only constrain it to the window when this host
        // participates in flex layout. Without that contract, root scroll views expand to their
        // full content height and never acquire an overflow range.
        div()
            .tab_group()
            .flex()
            .flex_col()
            .capture_key_down(cx.listener(|this, _, _, _| this.resources.shortcuts.begin()))
            .on_key_down(move |event, window, cx| {
                item_tooltips.dismiss(window);
                let modifiers = event.keystroke.modifiers;
                if event.keystroke.key != "tab"
                    || modifiers.control
                    || modifiers.alt
                    || modifiers.platform
                    || modifiers.function
                {
                    return;
                }
                if modifiers.shift {
                    cycle_focus(false, window, cx);
                } else {
                    cycle_focus(true, window, cx);
                }
                cx.stop_propagation();
            })
            .size_full()
            .bg(rgba(theme.background))
            .text_color(rgba(theme.text))
            .child(content)
            .child(crate::item_tooltip::frame_end(
                self.resources.item_tooltips.clone(),
            ))
            .when_some(toast_layer, |root, layer| root.child(layer))
    }
}

pub(crate) fn cycle_focus(forward: bool, window: &mut Window, cx: &mut App) {
    let step = |window: &mut Window, cx: &mut App| {
        if forward {
            window.focus_next(cx);
        } else {
            window.focus_prev(cx);
        }
    };

    let Some(trap) = gpui_base::active_focus_trap(window, cx) else {
        step(window, cx);
        return;
    };

    let before = window.focused(cx);
    step(window, cx);

    const MAX_STEPS: usize = 100;
    let mut steps = 0;
    while !trap.contains_focused(window, cx) && steps < MAX_STEPS {
        step(window, cx);
        steps += 1;
        if window.focused(cx) == before {
            break;
        }
    }

    if !trap.contains_focused(window, cx) {
        trap.focus(window, cx);
    }
}

fn active_dynamic_owners(snapshot: &ValidatedSnapshot) -> Vec<u32> {
    let mut owners = Vec::new();
    for node in &snapshot.nodes {
        if node.component != COMPONENT_DYNAMIC {
            continue;
        }
        let operations = snapshot.ops(node);
        let active = operations
            .iter()
            .rev()
            .find(|operation| operation.code == OP_DYNAMIC_ACTIVE)
            .is_some_and(|operation| operation.a != 0);
        if !active {
            continue;
        }
        let owner = operations
            .iter()
            .rev()
            .find(|operation| operation.code == OP_RESOURCE_OWNER)
            .map_or(0, |operation| operation.a as u32);
        if owner != 0 && !owners.contains(&owner) {
            owners.push(owner);
        }
    }
    owners
}

#[cfg(test)]
#[test]
#[ignore = "opt-in Release measurement; run eng/measure-native.ps1"]
fn native_workload_measurements_dynamic_discovery() {
    use crate::{
        native_workloads::{WorkloadArena, measure},
        semantic::COMPONENT_DIV,
    };
    for (static_nodes, dynamic_nodes, distinct_owners) in [
        (128, 0, 0),
        (16_384, 0, 0),
        (16_384, 1, 1),
        (16_384, 128, 1),
        (16_384, 128, 128),
        (16_384, 1024, 1024),
    ] {
        let mut arena = WorkloadArena::default();
        let root = arena.node(COMPONENT_DIV, None);
        for _ in 0..static_nodes {
            arena.node(COMPONENT_DIV, Some(root));
        }
        for index in 0..dynamic_nodes {
            let node = arena.node(COMPONENT_DYNAMIC, Some(root));
            arena.node(COMPONENT_DIV, Some(node));
            arena.op(node, OP_DYNAMIC_ACTIVE, 1);
            arena.op(
                node,
                OP_RESOURCE_OWNER,
                (index % distinct_owners + 1) as u64,
            );
        }
        let snapshot = arena.decode();
        let owners = active_dynamic_owners(&snapshot);
        assert_eq!(owners.len(), distinct_owners);
        println!(
            "dynamic static={static_nodes} wrappers={dynamic_nodes} owners={distinct_owners} snapshot_buffers={} result_capacity_bytes={}",
            snapshot.buffer_capacity_bytes(),
            owners.capacity() * size_of::<u32>()
        );
        measure("dynamic-discovery", 256, || {
            std::hint::black_box(active_dynamic_owners(std::hint::black_box(&snapshot)));
        });
    }
}

pub fn run(application_id: u64, callbacks: ManagedCallbacks) -> i32 {
    trace::init_from_env();
    let (sender, receiver) = async_channel::bounded(INGRESS_CAPACITY);
    let Ok(_application_registration) =
        ApplicationRegistration::new(application_id, ApplicationNotifier { sender })
    else {
        return -40;
    };

    let startup_status = unsafe {
        callbacks
            .application_started
            .expect("callbacks were validated before application startup")(application_id)
    };
    if startup_status != 0 {
        return startup_status;
    }

    let application_status = Arc::new(AtomicI32::new(0));
    let application_status_in_app = Arc::clone(&application_status);

    gpui_platform::application()
        .with_assets(crate::extension::NativeExtensionAssets)
        .run(move |cx: &mut App| {
            gpui_base::init(cx);
            crate::input::init(cx);
            let windows: ManagedWindows = Rc::new(RefCell::new(HashMap::new()));
            let initial_theme = NativeTheme::default();
            initial_theme.apply(cx);
            cx.set_global(initial_theme.resolved());
            crate::extension::initialize_providers(cx);
            crate::extension::apply_provider_themes(cx);
            let theme: SharedTheme = Rc::new(RefCell::new(initial_theme));

            let menu_status = Arc::clone(&application_status_in_app);
            cx.set_global(MenuGeneration::default());
            cx.on_action(move |action: &ManagedMenuAction, cx| {
                let status = dispatch_menu_action(action, application_id, callbacks, cx);
                record_status(&menu_status, status);
            });

            let closed_windows = Rc::clone(&windows);
            let closed_status = Arc::clone(&application_status_in_app);
            cx.on_window_closed(move |cx, _| {
                report_closed_windows(
                    cx,
                    application_id,
                    callbacks,
                    &closed_windows,
                    &closed_status,
                );
            })
            .detach();

            while let Ok(command) = receiver.try_recv() {
                apply_application_command(
                    command,
                    cx,
                    application_id,
                    callbacks,
                    &windows,
                    &theme,
                    &application_status_in_app,
                );
            }

            if windows.borrow().is_empty() {
                record_status(&application_status_in_app, -24);
                cx.quit();
                return;
            }

            let command_windows = Rc::clone(&windows);
            let command_theme = Rc::clone(&theme);
            let command_status = Arc::clone(&application_status_in_app);
            cx.spawn(async move |cx| {
                while let Ok(command) = receiver.recv().await {
                    cx.update(|cx| {
                        apply_application_command(
                            command,
                            cx,
                            application_id,
                            callbacks,
                            &command_windows,
                            &command_theme,
                            &command_status,
                        );
                    });
                }
            })
            .detach();

            cx.activate(true);
        });

    application_status.load(Ordering::Acquire)
}

fn apply_application_command(
    command: ApplicationCommand,
    cx: &mut App,
    application_id: u64,
    callbacks: ManagedCallbacks,
    windows: &ManagedWindows,
    theme: &SharedTheme,
    application_status: &AtomicI32,
) {
    match command {
        ApplicationCommand::SetMenuBar { menus, generation } => {
            cx.set_menus(menus.into_iter().map(|menu| convert_menu(menu, generation)));
            cx.set_global(MenuGeneration(generation));
            let status = unsafe {
                callbacks
                    .menu_applied
                    .expect("validated menu acknowledgement")(
                    application_id, generation
                )
            };
            record_status(application_status, status);
        }
        ApplicationCommand::SetTheme(next) => {
            next.apply(cx);
            *theme.borrow_mut() = next;
            cx.set_global(next.resolved());
            crate::extension::apply_provider_themes(cx);
            for entry in windows.borrow().values() {
                let Some(handle) = entry.handle.downcast::<ManagedView>() else {
                    continue;
                };
                let _ = handle.update(cx, |view, window, cx| {
                    view.resources.invalidate_managed_rendered_items();
                    view.invalidate(cx);
                    window.refresh();
                });
            }
        }
        ApplicationCommand::ManagedCodeUpdated => {
            for entry in windows.borrow().values() {
                let Some(handle) = entry.handle.downcast::<ManagedView>() else {
                    continue;
                };
                let _ = handle.update(cx, |view, window, cx| {
                    view.resources.invalidate_managed_rendered_items();
                    view.invalidate(cx);
                    window.refresh();
                });
            }
        }
        ApplicationCommand::EvictImage { path } => {
            // Unknown paths are a silent no-op: eviction is idempotent by design.
            for entry in windows.borrow().values() {
                let Some(handle) = entry.handle.downcast::<ManagedView>() else {
                    continue;
                };
                let _ = handle.update(cx, |view, window, cx| {
                    view.resources.evict_image(&path, window, cx);
                    view.invalidate(cx);
                    window.refresh();
                });
            }
        }
        ApplicationCommand::SetImageCacheBudget {
            max_bytes,
            max_entries,
        } => {
            crate::images::set_global_budget(max_bytes, max_entries);
            // The new budget applies lazily to every view's cache; force the next render to
            // reconcile so a shrunken budget trims promptly instead of waiting for a snapshot.
            for entry in windows.borrow().values() {
                let Some(handle) = entry.handle.downcast::<ManagedView>() else {
                    continue;
                };
                let _ = handle.update(cx, |view, window, cx| {
                    view.resources.note_image_budget_changed();
                    view.invalidate(cx);
                    window.refresh();
                });
            }
        }
        ApplicationCommand::Open {
            window_id,
            title,
            left,
            top,
            width,
            height,
            activate,
            title_bar_style,
            initial_state,
        } => {
            let result = open_managed_window(
                cx,
                windows,
                window_id,
                callbacks,
                &title,
                left.zip(top),
                width,
                height,
                activate,
                title_bar_style,
                initial_state,
                theme.clone(),
            );
            if let Err(status) = result {
                record_status(application_status, status);
                report_window_closed(
                    application_id,
                    window_id,
                    status,
                    callbacks,
                    application_status,
                );
                if windows.borrow().is_empty() {
                    cx.quit();
                }
            }
        }
        ApplicationCommand::Close(window_id) => {
            if let Some(handle) = managed_window_handle(windows, window_id) {
                let _ = handle.update(cx, |_, window, _| window.remove_window());
            }
        }
        ApplicationCommand::Activate(window_id) => {
            if let Some(handle) = managed_window_handle(windows, window_id) {
                let _ = handle.update(cx, |_, window, _| window.activate_window());
            }
        }
        ApplicationCommand::Minimize(window_id) => {
            if let Some(handle) = managed_window_handle(windows, window_id) {
                let _ = handle.update(cx, |_, window, _| window.minimize_window());
            }
        }
        ApplicationCommand::ToggleMaximize(window_id) => {
            if let Some(handle) = managed_window_handle(windows, window_id) {
                let _ = handle.update(cx, |_, window, _| toggle_window_maximize(window));
            }
        }
        ApplicationCommand::ToggleFullscreen(window_id) => {
            if let Some(handle) = managed_window_handle(windows, window_id) {
                let _ = handle.update(cx, |_, window, _| window.toggle_fullscreen());
            }
        }
        ApplicationCommand::ShowToast { window_id, toast } => {
            if let Some(handle) = managed_window_handle(windows, window_id)
                .and_then(|handle| handle.downcast::<ManagedView>())
            {
                let _ = handle.update(cx, |view, window, cx| view.show_toast(toast, window, cx));
            }
        }
        ApplicationCommand::DismissToast { window_id, id } => {
            if let Some(handle) = managed_window_handle(windows, window_id)
                .and_then(|handle| handle.downcast::<ManagedView>())
            {
                let _ = handle.update(cx, |view, _, cx| view.dismiss_toast(&id, cx));
            }
        }
        ApplicationCommand::ClearToasts(window_id) => {
            if let Some(handle) = managed_window_handle(windows, window_id)
                .and_then(|handle| handle.downcast::<ManagedView>())
            {
                let _ = handle.update(cx, |view, _, cx| view.clear_toasts(cx));
            }
        }
        ApplicationCommand::SetTitle { window_id, title } => {
            if let Some(handle) = managed_window_handle(windows, window_id) {
                let _ = handle.update(cx, |_, window, _| window.set_window_title(&title));
            }
        }
        ApplicationCommand::Resize {
            window_id,
            width,
            height,
        } => {
            if let Some(handle) = managed_window_handle(windows, window_id) {
                let _ = handle.update(cx, |_, window, _| {
                    window.resize(size(px(width), px(height)));
                });
            }
        }
    }
}

fn convert_menu(menu: ManagedMenu, generation: u64) -> Menu {
    Menu {
        name: menu.title.into(),
        items: menu
            .items
            .into_iter()
            .map(|item| convert_menu_item(item, generation))
            .collect(),
        disabled: false,
    }
}

fn convert_menu_item(item: ManagedMenuItem, generation: u64) -> MenuItem {
    match item {
        ManagedMenuItem::Separator => MenuItem::separator(),
        ManagedMenuItem::Action { id, title } => {
            MenuItem::action(title, ManagedMenuAction { id, generation })
        }
        ManagedMenuItem::Submenu(menu) => MenuItem::submenu(convert_menu(menu, generation)),
    }
}

fn managed_window_handle(windows: &ManagedWindows, window_id: u64) -> Option<AnyWindowHandle> {
    // Drop the RefCell borrow before calling into GPUI. remove_window can synchronously invoke
    // on_window_closed, which mutates this same registry.
    windows.borrow().get(&window_id).map(|entry| entry.handle)
}

fn toggle_window_maximize(window: &mut Window) {
    #[cfg(target_os = "windows")]
    if window.is_maximized() {
        use raw_window_handle::{HasWindowHandle, RawWindowHandle};
        use windows::Win32::{
            Foundation::HWND,
            UI::WindowsAndMessaging::{SW_RESTORE, ShowWindowAsync},
        };

        if let Ok(handle) = window.window_handle()
            && let RawWindowHandle::Win32(handle) = handle.as_raw()
        {
            let hwnd = HWND(handle.hwnd.get() as *mut core::ffi::c_void);
            let _ = unsafe { ShowWindowAsync(hwnd, SW_RESTORE) };
            return;
        }
    }

    window.zoom_window();
}

#[allow(clippy::too_many_arguments)]
fn open_managed_window(
    cx: &mut App,
    windows: &ManagedWindows,
    window_id: u64,
    callbacks: ManagedCallbacks,
    title: &str,
    position: Option<(f32, f32)>,
    width: f32,
    height: f32,
    activate: bool,
    title_bar_style: WindowTitleBarStyle,
    initial_state: WindowInitialState,
    theme: SharedTheme,
) -> Result<(), i32> {
    if windows.borrow().contains_key(&window_id) {
        return Err(-44);
    }

    let (sender, receiver) = async_channel::bounded(INGRESS_CAPACITY);
    let invalidate_pending = Arc::new(AtomicBool::new(false));
    let presence = Arc::new(Mutex::new(ResourcePresence::default()));
    let view_registration = ViewRegistration::new(
        window_id,
        ViewNotifier {
            sender,
            invalidate_pending: Arc::clone(&invalidate_pending),
            presence: Arc::clone(&presence),
        },
    )?;

    let bounds = position.map_or_else(
        || Bounds::centered(None, size(px(width), px(height)), cx),
        |(left, top)| Bounds::new(point(px(left), px(top)), size(px(width), px(height))),
    );
    let titlebar = match title_bar_style {
        WindowTitleBarStyle::System => Some(TitlebarOptions {
            title: Some(title.to_owned().into()),
            ..Default::default()
        }),
        WindowTitleBarStyle::Custom => Some(TitlebarOptions {
            title: Some(title.to_owned().into()),
            appears_transparent: true,
            ..Default::default()
        }),
        WindowTitleBarStyle::Hidden => None,
    };
    let window_decorations =
        (title_bar_style != WindowTitleBarStyle::System).then_some(WindowDecorations::Client);
    let window_bounds = match initial_state {
        WindowInitialState::Normal => WindowBounds::Windowed(bounds),
        WindowInitialState::Maximized => WindowBounds::Maximized(bounds),
        WindowInitialState::Fullscreen => WindowBounds::Fullscreen(bounds),
    };
    let handle = cx
        .open_window(
            WindowOptions {
                window_bounds: Some(window_bounds),
                titlebar,
                focus: activate,
                window_decorations,
                ..Default::default()
            },
            move |_, cx| {
                create_managed_view(
                    cx,
                    window_id,
                    callbacks,
                    receiver,
                    invalidate_pending,
                    presence,
                    theme,
                )
            },
        )
        .map_err(|_| -45)?;

    let handle = AnyWindowHandle::from(handle);
    windows.borrow_mut().insert(
        window_id,
        ManagedWindowRegistration {
            handle,
            _view_registration: view_registration,
        },
    );
    if title_bar_style == WindowTitleBarStyle::Hidden {
        let title = title.to_owned();
        let _ = handle.update(cx, move |_, window, _| window.set_window_title(&title));
    }
    if activate {
        let _ = handle.update(cx, |_, window, _| window.activate_window());
    }
    Ok(())
}

fn report_closed_windows(
    cx: &mut App,
    application_id: u64,
    callbacks: ManagedCallbacks,
    windows: &ManagedWindows,
    application_status: &AtomicI32,
) {
    let open: HashSet<_> = cx.windows().into_iter().collect();
    let closed: Vec<_> = windows
        .borrow()
        .iter()
        .filter_map(|(window_id, entry)| (!open.contains(&entry.handle)).then_some(*window_id))
        .collect();

    for window_id in closed {
        windows.borrow_mut().remove(&window_id);
        report_window_closed(application_id, window_id, 0, callbacks, application_status);
    }
    if windows.borrow().is_empty() {
        cx.quit();
    }
}

fn report_window_closed(
    application_id: u64,
    window_id: u64,
    native_status: i32,
    callbacks: ManagedCallbacks,
    application_status: &AtomicI32,
) {
    let status = unsafe {
        callbacks
            .window_closed
            .expect("callbacks were validated before application startup")(
            application_id,
            window_id,
            native_status,
        )
    };
    record_status(application_status, status);
}

fn record_status(application_status: &AtomicI32, status: i32) {
    if status != 0 {
        let _ = application_status.compare_exchange(0, status, Ordering::AcqRel, Ordering::Acquire);
    }
}

fn create_managed_view(
    cx: &mut App,
    view_id: u64,
    callbacks: ManagedCallbacks,
    receiver: Receiver<ViewMessage>,
    invalidate_pending: Arc<AtomicBool>,
    presence: Arc<Mutex<ResourcePresence>>,
    theme: SharedTheme,
) -> gpui::Entity<ManagedView> {
    let view = cx.new(|_| ManagedView::new(view_id, callbacks, theme, presence));
    let weak_view = view.downgrade();

    cx.spawn(async move |cx| {
        while let Ok(message) = receiver.recv().await {
            if weak_view
                .update(cx, |view, cx| match message {
                    ViewMessage::Invalidate => {
                        invalidate_pending.store(false, Ordering::Release);
                        view.invalidate(cx);
                    }
                    ViewMessage::InvalidateArtifacts(mut keys) => {
                        if view.deliver_artifact_invalidations(&mut keys) {
                            cx.notify();
                        }
                    }
                    ViewMessage::ResourceCommand(command, generation) => {
                        if view.deliver_resource_command(command, generation) {
                            cx.notify();
                        }
                    }
                    ViewMessage::ExtensionCommand(command, generation) => {
                        if view.deliver_extension_command(command, generation) {
                            cx.notify();
                        }
                    }
                })
                .is_err()
            {
                break;
            }
        }
    })
    .detach();

    view
}

#[cfg(test)]
mod tests {
    #[gpui::test]
    fn menu_replacement_acknowledges_and_filters_queued_old_actions(cx: &mut gpui::TestAppContext) {
        unsafe extern "C" fn applied(_: u64, generation: u64) -> i32 {
            if generation == 2 { 0 } else { -1 }
        }
        unsafe extern "C" fn action(_: u64, _: u64) -> i32 {
            17
        }
        cx.update(|cx| {
            let mut callbacks: ManagedCallbacks = unsafe { std::mem::zeroed() };
            callbacks.menu_applied = Some(applied);
            callbacks.menu_action = Some(action);
            cx.set_global(MenuGeneration(1));
            let old = ManagedMenuAction {
                id: 1,
                generation: 1,
            };
            assert_eq!(dispatch_menu_action(&old, 1, callbacks, cx), 17);
            let status = AtomicI32::new(0);
            apply_application_command(
                ApplicationCommand::SetMenuBar {
                    menus: vec![],
                    generation: 2,
                },
                cx,
                1,
                callbacks,
                &Rc::default(),
                &Rc::new(RefCell::new(NativeTheme::default())),
                &status,
            );
            assert_eq!(status.load(Ordering::Acquire), 0);
            assert_eq!(cx.global::<MenuGeneration>().0, 2);
            assert_eq!(dispatch_menu_action(&old, 1, callbacks, cx), 0);
            assert_eq!(
                dispatch_menu_action(
                    &ManagedMenuAction {
                        id: 2,
                        generation: 2
                    },
                    1,
                    callbacks,
                    cx
                ),
                17
            );
        });
    }
    use super::*;
    use gpui::FocusHandle;
    use gpui_base::FocusTrapElement as _;

    struct FocusTrapHarness {
        trap: FocusHandle,
        first: FocusHandle,
        last: FocusHandle,
        outside: FocusHandle,
    }

    impl Render for FocusTrapHarness {
        fn render(&mut self, _: &mut Window, _: &mut Context<Self>) -> impl IntoElement {
            div()
                .child(
                    div()
                        .track_focus(&self.trap)
                        .child(div().track_focus(&self.first))
                        .child(div().track_focus(&self.last))
                        .focus_trap("managed-overlay-test", &self.trap),
                )
                .child(div().track_focus(&self.outside))
        }
    }

    #[gpui::test]
    fn gpui_base_foundation_initializes(cx: &mut gpui::TestAppContext) {
        cx.update(|cx| {
            gpui_base::init(cx);

            assert!(cx.has_global::<gpui_base::Theme>());
            assert!(cx.has_global::<gpui_base::GlobalState>());
        });
    }

    #[gpui::test]
    fn managed_root_is_a_flex_viewport_for_growing_scroll_content(cx: &mut gpui::TestAppContext) {
        use crate::{
            native_workloads::WorkloadArena,
            resources::ResourceKey,
            semantic::{
                COMPONENT_DIV, COMPONENT_SCROLL, OP_FLEX_GROW, OP_HEIGHT_PX, OP_RESOURCE_OWNER,
                OP_V_STACK, OP_WIDTH_PERCENT,
            },
        };

        let mut arena = WorkloadArena::default();
        let root = arena.node(COMPONENT_DIV, None);
        arena.op(root, OP_V_STACK, 0);
        arena.op(root, OP_FLEX_GROW, 1f32.to_bits() as u64);
        arena.op(root, OP_WIDTH_PERCENT, 100f32.to_bits() as u64);
        let scroll = arena.node_with_data(COMPONENT_SCROLL, Some(root), "catalog-scroll");
        arena.op(scroll, OP_RESOURCE_OWNER, 1);
        arena.op(scroll, OP_FLEX_GROW, 1f32.to_bits() as u64);
        arena.op(scroll, OP_WIDTH_PERCENT, 100f32.to_bits() as u64);
        let body = arena.node(COMPONENT_DIV, Some(scroll));
        arena.op(body, OP_V_STACK, 0);
        for _ in 0..8 {
            let row = arena.node(COMPONENT_DIV, Some(body));
            arena.op(row, OP_HEIGHT_PX, 100f32.to_bits() as u64);
        }
        let snapshot = arena.decode();
        let (view, cx) = cx.add_window_view(move |_, _| {
            let callbacks = unsafe { std::mem::zeroed() };
            let mut view = ManagedView::new(1, callbacks, Default::default(), Default::default());
            view.snapshot = snapshot;
            view.has_snapshot = true;
            view.dirty = false;
            view
        });
        cx.simulate_resize(gpui::size(px(320.), px(240.)));
        cx.update(|window, cx| window.draw(cx).clear(cx));

        let resource = view.read_with(cx, |view, _| {
            view.resources
                .scroll_resource(&ResourceKey::new(1, "catalog-scroll".into()))
        });
        assert!(resource.handle.max_offset().y > px(0.));
    }

    #[gpui::test]
    fn root_focus_navigation_wraps_inside_foundation_trap(cx: &mut gpui::TestAppContext) {
        cx.update(gpui_base::init);
        let (view, cx) = cx.add_window_view(|_, cx| FocusTrapHarness {
            trap: cx.focus_handle().tab_stop(false),
            first: cx.focus_handle().tab_stop(true),
            last: cx.focus_handle().tab_stop(true),
            outside: cx.focus_handle().tab_stop(true),
        });
        cx.update(|window, cx| window.draw(cx).clear(cx));

        cx.update(|window, cx| {
            let last = view.read(cx).last.clone();
            last.focus(window, cx);
            cycle_focus(true, window, cx);
            assert!(view.read(cx).first.contains_focused(window, cx));

            cycle_focus(false, window, cx);
            assert!(view.read(cx).last.contains_focused(window, cx));
        });
    }

    #[test]
    fn dynamic_frame_owners_are_active_and_deduplicated() {
        use crate::{
            abi::{ChildRecord, NodeRecord, OpRecord, RenderArena},
            semantic::{COMPONENT_DIV, ValueKind},
        };

        let mut nodes = vec![
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DYNAMIC,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DYNAMIC,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
        ];
        let mut operations = vec![
            OpRecord {
                node: 1,
                code: OP_RESOURCE_OWNER,
                value_kind: ValueKind::U32 as u16,
                a: 7,
                ..Default::default()
            },
            OpRecord {
                node: 1,
                code: OP_DYNAMIC_ACTIVE,
                value_kind: ValueKind::U32 as u16,
                a: 1,
                ..Default::default()
            },
            OpRecord {
                node: 3,
                code: OP_RESOURCE_OWNER,
                value_kind: ValueKind::U32 as u16,
                a: 7,
                ..Default::default()
            },
            OpRecord {
                node: 3,
                code: OP_DYNAMIC_ACTIVE,
                value_kind: ValueKind::U32 as u16,
                a: 1,
                ..Default::default()
            },
        ];
        let mut children = vec![
            ChildRecord {
                parent: 0,
                child: 1,
            },
            ChildRecord {
                parent: 1,
                child: 2,
            },
            ChildRecord {
                parent: 0,
                child: 3,
            },
            ChildRecord {
                parent: 3,
                child: 4,
            },
        ];
        let mut utf8 = Vec::<u8>::new();
        let arena = RenderArena {
            nodes: nodes.as_mut_ptr(),
            node_length: nodes.len() as i32,
            node_capacity: nodes.len() as i32,
            ops: operations.as_mut_ptr(),
            op_length: operations.len() as i32,
            op_capacity: operations.len() as i32,
            children: children.as_mut_ptr(),
            child_length: children.len() as i32,
            child_capacity: children.len() as i32,
            utf8: utf8.as_mut_ptr(),
            utf8_length: 0,
            utf8_capacity: 0,
            generation: 1,
            flags: 0,
            required_node_capacity: 0,
            required_op_capacity: 0,
            required_child_capacity: 0,
            required_utf8_capacity: 0,
        };
        let mut snapshot = ValidatedSnapshot::default();
        snapshot
            .decode_into(
                &arena,
                0,
                &mut RetainedStrings::default(),
                &mut SnapshotScratch::default(),
            )
            .unwrap();

        assert_eq!(active_dynamic_owners(&snapshot), vec![7]);
    }

    #[test]
    fn full_application_queue_rejects_new_commands_and_preserves_fifo() {
        let application_id = u64::MAX - 10;
        let (sender, receiver) = async_channel::bounded(INGRESS_CAPACITY);
        let _registration =
            ApplicationRegistration::new(application_id, ApplicationNotifier { sender }).unwrap();
        for id in 0..INGRESS_CAPACITY as u64 {
            assert_eq!(
                dispatch_application_command(application_id, ApplicationCommand::Close(id)),
                0
            );
        }
        assert_eq!(
            dispatch_application_command(application_id, ApplicationCommand::Close(u64::MAX)),
            -43
        );
        for id in 0..INGRESS_CAPACITY as u64 {
            assert!(
                matches!(receiver.try_recv(), Ok(ApplicationCommand::Close(received)) if received == id)
            );
        }
        assert!(receiver.is_empty());
        assert_eq!(
            dispatch_application_command(application_id, ApplicationCommand::ManagedCodeUpdated),
            0
        );
    }

    #[test]
    fn full_window_queue_resets_notification_latch_for_retry() {
        let view_id = u64::MAX - 10;
        let (sender, receiver) = async_channel::bounded(INGRESS_CAPACITY);
        let pending = Arc::new(AtomicBool::new(false));
        let _registration = ViewRegistration::new(
            view_id,
            ViewNotifier {
                sender,
                invalidate_pending: pending.clone(),
                presence: Arc::default(),
            },
        )
        .unwrap();
        for _ in 0..INGRESS_CAPACITY {
            assert_eq!(invalidate_artifacts(view_id, vec![]), 0);
        }
        assert_eq!(invalidate_artifacts(view_id, vec![]), -33);
        assert_eq!(notify(view_id), -33);
        assert!(!pending.load(Ordering::Acquire));
        for _ in 0..INGRESS_CAPACITY {
            assert!(matches!(
                receiver.try_recv(),
                Ok(ViewMessage::InvalidateArtifacts(_))
            ));
        }
        assert!(receiver.is_empty());
        assert_eq!(notify(view_id), 0);
        assert!(pending.load(Ordering::Acquire));
        assert_eq!(notify(view_id), 0);
        assert!(matches!(receiver.try_recv(), Ok(ViewMessage::Invalidate)));
        assert!(receiver.is_empty());
    }

    #[test]
    fn notifications_are_coalesced_and_registration_is_scoped() {
        let view_id = u64::MAX;
        let (sender, receiver) = async_channel::unbounded();
        let pending = Arc::new(AtomicBool::new(false));
        let registration = ViewRegistration::new(
            view_id,
            ViewNotifier {
                sender,
                invalidate_pending: Arc::clone(&pending),
                presence: Arc::default(),
            },
        )
        .unwrap();

        after_detached_callback(view_id, 0);
        assert!(receiver.try_recv().is_err());
        after_detached_callback(view_id, -111);
        after_detached_callback(view_id, -111);
        assert!(matches!(receiver.try_recv(), Ok(ViewMessage::Invalidate)));
        assert!(receiver.try_recv().is_err());
        pending.store(false, Ordering::Release);

        assert_eq!(notify(view_id), 0);
        assert_eq!(notify(view_id), 0);
        assert!(matches!(receiver.try_recv(), Ok(ViewMessage::Invalidate)));
        assert!(receiver.try_recv().is_err());

        pending.store(false, Ordering::Release);
        let keys = vec![crate::abi::NativeArtifactKey {
            source: 1,
            artifact: 2,
        }];
        assert_eq!(invalidate_artifacts(view_id, keys.clone()), 0);
        let Ok(ViewMessage::InvalidateArtifacts(received)) = receiver.try_recv() else {
            panic!("missing artifact message");
        };
        assert_eq!(received, keys);
        assert!(!pending.load(Ordering::Acquire));
        assert_eq!(notify(view_id), 0);
        assert!(matches!(receiver.try_recv(), Ok(ViewMessage::Invalidate)));

        drop(registration);
        assert_eq!(notify(view_id), -30);
    }

    #[test]
    fn command_ingress_requires_presence_and_stamps_its_generation() {
        use crate::semantic::{COMMAND_INPUT_SET_VALUE, RESOURCE_INPUT};
        use crate::{extension, resources::ResourceKey};

        let view_id = u64::MAX - 1;
        let (sender, receiver) = async_channel::unbounded();
        let presence = Arc::new(Mutex::new(ResourcePresence::default()));
        let _registration = ViewRegistration::new(
            view_id,
            ViewNotifier {
                sender,
                invalidate_pending: Arc::default(),
                presence: presence.clone(),
            },
        )
        .unwrap();
        let key = ResourceKey::new(7, "入力".into());
        let command = ResourceCommand {
            key: key.clone(),
            resource_kind: RESOURCE_INPUT,
            command: COMMAND_INPUT_SET_VALUE,
            a: 0,
            b: 0,
            data: "界".into(),
        };
        let extension_key = extension::resource_key(7, "test", "editor", "document", 1, 17);
        let extension_command = NativeExtensionCommand {
            resource_key: extension_key.clone(),
            command: 1,
            flags: 0,
            expected_revision: 0,
            payload: Arc::from(&b"document"[..]),
        };
        assert_eq!(dispatch_command(view_id, command.clone()), -34);
        assert_eq!(
            dispatch_extension_command(view_id, extension_command.clone()),
            -34
        );
        assert!(receiver.try_recv().is_err());

        let base = HashSet::from([(RESOURCE_INPUT, key)]);
        let extensions = HashSet::from([extension_key]);
        presence.lock().unwrap().accept(&base, &extensions, 11);
        assert_eq!(dispatch_command(view_id, command.clone()), 0);
        assert_eq!(
            dispatch_extension_command(view_id, extension_command.clone()),
            0
        );
        // A later snapshot preserving the declaration must preserve the presence generation.
        presence.lock().unwrap().accept(&base, &extensions, 12);
        let Ok(ViewMessage::ResourceCommand(queued, generation)) = receiver.try_recv() else {
            panic!("expected base command");
        };
        assert_eq!(generation, 11);
        assert_eq!(queued.data.as_ref(), "界");
        let Ok(ViewMessage::ExtensionCommand(_, generation)) = receiver.try_recv() else {
            panic!("expected extension command");
        };
        assert_eq!(generation, 11);

        presence
            .lock()
            .unwrap()
            .accept(&HashSet::new(), &HashSet::new(), 13);
        assert_eq!(dispatch_command(view_id, command.clone()), -34);
        assert_eq!(
            dispatch_extension_command(view_id, extension_command.clone()),
            -34
        );
        presence.lock().unwrap().accept(&base, &extensions, 14);
        assert_eq!(dispatch_command(view_id, command), 0);
        assert_eq!(dispatch_extension_command(view_id, extension_command), 0);
        assert!(matches!(
            receiver.try_recv(),
            Ok(ViewMessage::ResourceCommand(_, 14))
        ));
        assert!(matches!(
            receiver.try_recv(),
            Ok(ViewMessage::ExtensionCommand(_, 14))
        ));
    }

    #[test]
    fn queued_commands_cannot_reach_a_recreated_resource() {
        use crate::semantic::{
            COMMAND_LIST_SCROLL_TO_ITEM, COMMAND_SCROLL_TO_OFFSET, RESOURCE_LIST, RESOURCE_SCROLL,
        };
        use crate::{extension, resources::ResourceKey};

        let callbacks = ManagedCallbacks {
            struct_size: std::mem::size_of::<ManagedCallbacks>() as u32,
            render: None,
            click: None,
            list_render_range: None,
            control_event: None,
            application_started: None,
            window_closed: None,
            menu_action: None,
            menu_applied: None,
            dynamic_frame: None,
            render_completed: None,
            release_artifact: None,
            accept_artifact: None,
        };
        let presence = Arc::new(Mutex::new(ResourcePresence::default()));
        let mut view = ManagedView::new(7, callbacks, Rc::default(), presence.clone());
        view.dirty = false;
        assert!(
            !view.deliver_artifact_invalidations(&mut [crate::abi::NativeArtifactKey {
                source: 1,
                artifact: 1
            }])
        );
        assert!(!view.dirty);
        let key = ResourceKey::new(7, "scroll".into());
        let command = ResourceCommand {
            key: key.clone(),
            resource_kind: RESOURCE_SCROLL,
            command: COMMAND_SCROLL_TO_OFFSET,
            a: 10f32.to_bits() as u64,
            b: 20f32.to_bits() as u64,
            data: "".into(),
        };
        let extension_key = extension::resource_key(7, "test", "editor", "document", 1, 17);
        let extension_command = NativeExtensionCommand {
            resource_key: extension_key.clone(),
            command: 1,
            flags: 0,
            expected_revision: 0,
            payload: Arc::from(&b"document"[..]),
        };
        let base = HashSet::from([(RESOURCE_SCROLL, key.clone())]);
        let extensions = HashSet::from([extension_key.clone()]);
        presence.lock().unwrap().accept(&base, &extensions, 1);
        presence
            .lock()
            .unwrap()
            .accept(&HashSet::new(), &HashSet::new(), 2);
        presence.lock().unwrap().accept(&base, &extensions, 3);

        assert!(!view.deliver_resource_command(command.clone(), 1));
        assert!(!view.deliver_extension_command(extension_command.clone(), 1));
        assert_eq!(
            view.resources.scroll_resource(&key).handle.offset(),
            point(px(0.), px(0.))
        );
        assert!(
            view.resources
                .extensions()
                .take_commands(&extension_key)
                .is_empty()
        );
        assert!(view.deliver_resource_command(command, 3));
        assert!(view.deliver_extension_command(extension_command, 3));
        assert_eq!(
            view.resources.scroll_resource(&key).handle.offset(),
            point(px(-10.), px(-20.))
        );
        assert_eq!(
            view.resources
                .extensions()
                .take_commands(&extension_key)
                .len(),
            1
        );

        let list_key = ResourceKey::new(7, "rows".into());
        let list_presence = HashSet::from([(RESOURCE_LIST, list_key.clone())]);
        presence
            .lock()
            .unwrap()
            .accept(&list_presence, &HashSet::new(), 4);
        presence
            .lock()
            .unwrap()
            .accept(&HashSet::new(), &HashSet::new(), 5);
        presence
            .lock()
            .unwrap()
            .accept(&list_presence, &HashSet::new(), 6);
        let config = crate::collections::ListConfiguration {
            tooltip_token: 0,
            tooltip: Default::default(),
            item_count: 100,
            renderer_token: 1,
            activation_token: 0,
            selection_token: 0,
            batch_size: 48,
            overdraw: px(240.),
            alignment: gpui::ListAlignment::Top,
            estimated_item_extent: px(40.),
            orientation: crate::collections::ListOrientation::Vertical,
            content_revision: Some(1),
            scrollbar: crate::scrolling::ScrollbarMetrics::new(px(8.), false),
            projection_revision: None,
        };
        let list = view.resources.list_resource(&list_key, &config, 6);
        let command = ResourceCommand {
            key: list_key,
            resource_kind: RESOURCE_LIST,
            command: COMMAND_LIST_SCROLL_TO_ITEM,
            a: 50,
            b: 0,
            data: "".into(),
        };
        assert!(!view.deliver_resource_command(command.clone(), 4));
        assert_eq!(
            list.borrow().state.scroll_px_offset_for_scrollbar().y,
            px(0.)
        );
        assert!(view.deliver_resource_command(command, 6));
        assert_eq!(
            list.borrow().state.scroll_px_offset_for_scrollbar().y,
            px(-2_000.)
        );
    }

    #[test]
    fn application_registration_routes_commands_and_is_scoped() {
        let application_id = u64::MAX;
        let (sender, receiver) = async_channel::unbounded();
        let registration =
            ApplicationRegistration::new(application_id, ApplicationNotifier { sender }).unwrap();

        assert_eq!(
            dispatch_application_command(application_id, ApplicationCommand::Close(7)),
            0
        );
        assert!(matches!(
            receiver.try_recv(),
            Ok(ApplicationCommand::Close(7))
        ));

        assert_eq!(
            dispatch_application_command(application_id, ApplicationCommand::ManagedCodeUpdated),
            0
        );
        assert!(matches!(
            receiver.try_recv(),
            Ok(ApplicationCommand::ManagedCodeUpdated)
        ));

        drop(registration);
        assert_eq!(
            dispatch_application_command(application_id, ApplicationCommand::Close(7)),
            -40
        );
    }
}
