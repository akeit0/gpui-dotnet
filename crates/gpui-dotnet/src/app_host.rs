use std::{
    cell::RefCell,
    collections::{HashMap, HashSet},
    rc::Rc,
    sync::{
        Arc, Mutex, OnceLock,
        atomic::{AtomicBool, AtomicI32, Ordering},
    },
};

use async_channel::{Receiver, Sender, TrySendError};
use gpui::{
    AnyWindowHandle, App, AppContext, Bounds, Context, IntoElement, Menu, MenuItem, Render,
    TitlebarOptions, Window, WindowBounds, WindowDecorations, WindowOptions, div, point,
    prelude::*, px, rgba, size,
};

use crate::{
    abi::ManagedCallbacks,
    arena::OwnedRenderArena,
    extension::NativeExtensionCommand,
    overlay::OverlayStack,
    popover_menu::PopoverMenuGroup,
    resources::{ResourceCommand, ResourceStore},
    semantic::{COMPONENT_DYNAMIC, OP_DYNAMIC_ACTIVE, OP_RESOURCE_OWNER},
    snapshot::{RetainedStrings, SnapshotScratch, ValidatedSnapshot},
    theme::{NativeTheme, SharedTheme},
    trace,
};

pub(crate) struct ManagedView {
    pub(crate) view_id: u64,
    pub(crate) callbacks: ManagedCallbacks,
    arena: OwnedRenderArena,
    retained_strings: RetainedStrings,
    pub(crate) snapshot: ValidatedSnapshot,
    snapshot_scratch: SnapshotScratch,
    has_snapshot: bool,
    pub(crate) snapshot_revision: u64,
    dirty: bool,
    pub(crate) error: Option<String>,
    pub(crate) resources: Rc<ResourceStore>,
    pub(crate) popover_menus: Rc<PopoverMenuGroup>,
    pub(crate) overlay_stack: Rc<OverlayStack>,
    pub(crate) theme: SharedTheme,
}

enum ViewMessage {
    Invalidate,
    ResourceCommand(ResourceCommand),
    ExtensionCommand(NativeExtensionCommand),
}

#[derive(Clone)]
struct ViewNotifier {
    sender: Sender<ViewMessage>,
    invalidate_pending: Arc<AtomicBool>,
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
    SetMenuBar(Vec<ManagedMenu>),
    SetTheme(NativeTheme),
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
    },
    Close(u64),
    Activate(u64),
    Minimize(u64),
    ToggleMaximize(u64),
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
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub(crate) enum WindowTitleBarStyle {
    System,
    Custom,
    Hidden,
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
    let sender = {
        let Ok(notifiers) = view_notifiers().lock() else {
            return -32;
        };
        let Some(notifier) = notifiers.get(&view_id) else {
            return -30;
        };
        notifier.sender.clone()
    };
    match sender.try_send(ViewMessage::ResourceCommand(command)) {
        Ok(()) => 0,
        Err(TrySendError::Full(_)) => -33,
        Err(TrySendError::Closed(_)) => -31,
    }
}

pub(crate) fn dispatch_extension_command(view_id: u64, command: NativeExtensionCommand) -> i32 {
    let sender = {
        let Ok(notifiers) = view_notifiers().lock() else {
            return -32;
        };
        let Some(notifier) = notifiers.get(&view_id) else {
            return -30;
        };
        notifier.sender.clone()
    };
    match sender.try_send(ViewMessage::ExtensionCommand(command)) {
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
    fn new(view_id: u64, callbacks: ManagedCallbacks, theme: SharedTheme) -> Self {
        Self {
            view_id,
            callbacks,
            arena: OwnedRenderArena::new(),
            retained_strings: RetainedStrings::default(),
            snapshot: ValidatedSnapshot::default(),
            snapshot_scratch: SnapshotScratch::default(),
            has_snapshot: false,
            snapshot_revision: 0,
            dirty: true,
            error: None,
            resources: Rc::new(ResourceStore::new(view_id, callbacks, theme.clone())),
            popover_menus: Rc::new(PopoverMenuGroup::default()),
            overlay_stack: OverlayStack::new(),
            theme,
        }
    }

    fn refresh_if_dirty(&mut self) {
        if !self.dirty {
            return;
        }

        self.dirty = false;
        self.error = None;
        let mut root = 0;
        let render = self
            .callbacks
            .render
            .expect("callbacks were validated before application startup");
        let view_id = self.view_id;
        let status = {
            let _stage = trace::span(trace::Stage::ManagedRender);
            self.arena
                .render_with_growth_retry(|arena| unsafe { render(view_id, arena, &mut root) })
                .unwrap_or_else(|status| status)
        };

        if status != 0 {
            self.error = Some(format!("Managed render failed with status {status}."));
            return;
        }

        let decode_result = {
            let _stage = trace::span(trace::Stage::SnapshotDecode);
            self.snapshot.decode_into(
                self.arena.as_native(),
                root,
                &mut self.retained_strings,
                &mut self.snapshot_scratch,
            )
        };
        match decode_result {
            Ok(()) => {
                self.has_snapshot = true;
                self.snapshot_revision = self.snapshot_revision.wrapping_add(1).max(1);
                let _stage = trace::span(trace::Stage::Retain);
                self.resources.retain_snapshot(&self.snapshot);
            }
            Err(status) => {
                self.error = Some(format!(
                    "Render snapshot validation failed with status {status}."
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
        if status != 0 {
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

    /// Cumulative list cache telemetry across every active list/table row engine, appended to
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
            sums[6] += telemetry.rendered_rows;
        }
        [
            ("engines", engines as u64),
            ("loads", sums[0]),
            ("hits", sums[1]),
            ("evict", sums[2]),
            ("inval", sums[3]),
            ("full", sums[4]),
            ("cross", sums[5]),
            ("rows", sums[6]),
        ]
    }
}

impl Render for ManagedView {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        self.refresh_if_dirty();
        self.overlay_stack.begin_frame();
        let theme = *self.theme.borrow();
        let dynamic_owners = if self.error.is_none() && self.has_snapshot {
            active_dynamic_owners(&self.snapshot)
        } else {
            Vec::new()
        };

        let content = {
            let _stage = trace::span(trace::Stage::Materialize);
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

        if trace::enabled() {
            trace::end_frame(&self.list_telemetry_sums());
        }
        self.schedule_dynamic_frame(dynamic_owners, window, cx);

        div()
            .tab_group()
            .on_key_down(|event, window, cx| {
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
    }
}

fn cycle_focus(forward: bool, window: &mut Window, cx: &mut App) {
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

pub fn run(application_id: u64, callbacks: ManagedCallbacks) -> i32 {
    trace::init_from_env();
    let (sender, receiver) = async_channel::unbounded();
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
        .with_assets(())
        .run(move |cx: &mut App| {
            gpui_base::init(cx);
            crate::input::init(cx);
            let windows: ManagedWindows = Rc::new(RefCell::new(HashMap::new()));
            let initial_theme = NativeTheme::default();
            initial_theme.apply(cx);
            let theme: SharedTheme = Rc::new(RefCell::new(initial_theme));

            let menu_status = Arc::clone(&application_status_in_app);
            cx.on_action(move |action: &ManagedMenuAction, _cx| {
                let status = unsafe {
                    callbacks
                        .menu_action
                        .expect("callbacks were validated before application startup")(
                        application_id,
                        action.id,
                    )
                };
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
        ApplicationCommand::SetMenuBar(menus) => {
            cx.set_menus(menus.into_iter().map(convert_menu));
        }
        ApplicationCommand::SetTheme(next) => {
            next.apply(cx);
            *theme.borrow_mut() = next;
            for entry in windows.borrow().values() {
                let Some(handle) = entry.handle.downcast::<ManagedView>() else {
                    continue;
                };
                let _ = handle.update(cx, |view, window, cx| {
                    view.resources.invalidate_managed_rendered_rows();
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
                    view.resources.invalidate_managed_rendered_rows();
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

fn convert_menu(menu: ManagedMenu) -> Menu {
    Menu {
        name: menu.title.into(),
        items: menu.items.into_iter().map(convert_menu_item).collect(),
        disabled: false,
    }
}

fn convert_menu_item(item: ManagedMenuItem) -> MenuItem {
    match item {
        ManagedMenuItem::Separator => MenuItem::separator(),
        ManagedMenuItem::Action { id, title } => MenuItem::action(title, ManagedMenuAction { id }),
        ManagedMenuItem::Submenu(menu) => MenuItem::submenu(convert_menu(menu)),
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
    theme: SharedTheme,
) -> Result<(), i32> {
    if windows.borrow().contains_key(&window_id) {
        return Err(-44);
    }

    let (sender, receiver) = async_channel::unbounded();
    let invalidate_pending = Arc::new(AtomicBool::new(false));
    let view_registration = ViewRegistration::new(
        window_id,
        ViewNotifier {
            sender,
            invalidate_pending: Arc::clone(&invalidate_pending),
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
    let handle = cx
        .open_window(
            WindowOptions {
                window_bounds: Some(WindowBounds::Windowed(bounds)),
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
    theme: SharedTheme,
) -> gpui::Entity<ManagedView> {
    let view = cx.new(|_| ManagedView::new(view_id, callbacks, theme));
    let weak_view = view.downgrade();

    cx.spawn(async move |cx| {
        while let Ok(message) = receiver.recv().await {
            if weak_view
                .update(cx, |view, cx| match message {
                    ViewMessage::Invalidate => {
                        invalidate_pending.store(false, Ordering::Release);
                        view.invalidate(cx);
                    }
                    ViewMessage::ResourceCommand(command) => {
                        let notify_native_only = command.resource_kind == 1
                            || command.resource_kind == 3
                            || (command.resource_kind == 2 && command.command == 10);
                        view.resources.dispatch(command);
                        if notify_native_only {
                            cx.notify();
                        }
                    }
                    ViewMessage::ExtensionCommand(command) => {
                        view.resources.extensions().enqueue_command(command);
                        cx.notify();
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
    fn notifications_are_coalesced_and_registration_is_scoped() {
        let view_id = u64::MAX;
        let (sender, receiver) = async_channel::unbounded();
        let pending = Arc::new(AtomicBool::new(false));
        let registration = ViewRegistration::new(
            view_id,
            ViewNotifier {
                sender,
                invalidate_pending: Arc::clone(&pending),
            },
        )
        .unwrap();

        assert_eq!(notify(view_id), 0);
        assert_eq!(notify(view_id), 0);
        assert!(matches!(receiver.try_recv(), Ok(ViewMessage::Invalidate)));
        assert!(receiver.try_recv().is_err());

        pending.store(false, Ordering::Release);
        assert_eq!(notify(view_id), 0);
        assert!(matches!(receiver.try_recv(), Ok(ViewMessage::Invalidate)));

        drop(registration);
        assert_eq!(notify(view_id), -30);
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
