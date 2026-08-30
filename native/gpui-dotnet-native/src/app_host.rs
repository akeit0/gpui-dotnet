use std::{
    collections::HashMap,
    rc::Rc,
    sync::{
        Arc, Mutex, OnceLock,
        atomic::{AtomicBool, AtomicI32, Ordering},
    },
};

use async_channel::{Receiver, Sender, TrySendError};
use gpui::{
    App, Application, Bounds, Context, IntoElement, Render, TitlebarOptions, Window, WindowBounds,
    WindowOptions, div, prelude::*, px, rgb, size,
};

use crate::{
    abi::ManagedCallbacks,
    arena::OwnedRenderArena,
    resources::{ResourceCommand, ResourceStore},
    snapshot::{RetainedStrings, SnapshotScratch, ValidatedSnapshot},
};

pub(crate) struct ManagedView {
    pub(crate) view_id: u64,
    pub(crate) callbacks: ManagedCallbacks,
    arena: OwnedRenderArena,
    retained_strings: RetainedStrings,
    snapshot: ValidatedSnapshot,
    snapshot_scratch: SnapshotScratch,
    has_snapshot: bool,
    pub(crate) snapshot_revision: u64,
    dirty: bool,
    pub(crate) error: Option<String>,
    pub(crate) resources: Rc<ResourceStore>,
}

enum ViewMessage {
    Invalidate,
    ResourceCommand(ResourceCommand),
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

impl ManagedView {
    fn new(view_id: u64, callbacks: ManagedCallbacks) -> Self {
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
            resources: Rc::new(ResourceStore::new(view_id, callbacks)),
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
        let status = self
            .arena
            .render_with_growth_retry(|arena| unsafe { render(view_id, arena, &mut root) })
            .unwrap_or_else(|status| status);

        if status != 0 {
            self.error = Some(format!("Managed render failed with status {status}."));
            return;
        }

        match self.snapshot.decode_into(
            self.arena.as_native(),
            root,
            &mut self.retained_strings,
            &mut self.snapshot_scratch,
        ) {
            Ok(()) => {
                self.has_snapshot = true;
                self.snapshot_revision = self.snapshot_revision.wrapping_add(1).max(1);
                self.resources.retain_snapshot(&self.snapshot);
            },
            Err(status) => {
                self.error = Some(format!(
                    "Render snapshot validation failed with status {status}."
                ));
            }
        }
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
}

impl Render for ManagedView {
    fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        self.refresh_if_dirty();

        let content = if let Some(error) = &self.error {
            div()
                .p(px(20.0))
                .text_color(rgb(0xb42318))
                .child(error.clone())
                .into_any_element()
        } else if self.has_snapshot {
            self.materialize_node(self.snapshot.root, &self.snapshot, cx)
        } else {
            div()
                .child("No managed snapshot was published.")
                .into_any_element()
        };

        div()
            .size_full()
            .bg(rgb(0xffffff))
            .text_color(rgb(0x182230))
            .child(content)
    }
}

pub fn run(view_id: u64, callbacks: ManagedCallbacks, title: &str, width: f32, height: f32) -> i32 {
    let title = title.to_owned();
    let (sender, receiver) = async_channel::unbounded();
    let invalidate_pending = Arc::new(AtomicBool::new(false));
    let notifier = ViewNotifier {
        sender,
        invalidate_pending: Arc::clone(&invalidate_pending),
    };
    let Ok(_registration) = ViewRegistration::new(view_id, notifier) else {
        return -30;
    };

    let shutdown_status = Arc::new(AtomicI32::new(0));
    let shutdown_status_in_app = Arc::clone(&shutdown_status);

    Application::new().run(move |cx: &mut App| {
        let shutdown_status = Arc::clone(&shutdown_status_in_app);
        let shutdown_callbacks = callbacks;
        cx.on_window_closed(move |cx| {
            if cx.windows().is_empty() {
                let status = unsafe {
                    shutdown_callbacks
                        .shutdown
                        .expect("callbacks were validated before application startup")(view_id)
                };
                shutdown_status.store(status, Ordering::Release);
                cx.quit();
            }
        })
        .detach();

        let bounds = Bounds::centered(None, size(px(width), px(height)), cx);
        cx.open_window(
            WindowOptions {
                window_bounds: Some(WindowBounds::Windowed(bounds)),
                titlebar: Some(TitlebarOptions {
                    title: Some(title.clone().into()),
                    ..Default::default()
                }),
                ..Default::default()
            },
            move |_, cx| create_managed_view(cx, view_id, callbacks, receiver, invalidate_pending),
        )
        .expect("failed to open the GPUI window");
        cx.activate(true);
    });

    shutdown_status.load(Ordering::Acquire)
}

fn create_managed_view(
    cx: &mut App,
    view_id: u64,
    callbacks: ManagedCallbacks,
    receiver: Receiver<ViewMessage>,
    invalidate_pending: Arc<AtomicBool>,
) -> gpui::Entity<ManagedView> {
    let view = cx.new(|_| ManagedView::new(view_id, callbacks));
    let weak_view = view.downgrade();

    cx.spawn(async move |cx| {
        while let Ok(message) = receiver.recv().await {
            if weak_view
                .update(cx, |view, cx| match message {
                    ViewMessage::Invalidate => {
                        invalidate_pending.store(false, Ordering::Release);
                        view.invalidate(cx);
                    },
                    ViewMessage::ResourceCommand(command) => {
                        let notify_native_only = command.resource_kind == 1
                            || (command.resource_kind == 2 && command.command == 10);
                        view.resources.dispatch(command);
                        if notify_native_only {
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
    use super::*;

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
}
