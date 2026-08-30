use std::{
    collections::HashMap,
    sync::{
        Arc, Mutex, OnceLock,
        atomic::{AtomicI32, Ordering},
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
    dirty: bool,
    pub(crate) error: Option<String>,
}

static VIEW_NOTIFIERS: OnceLock<Mutex<HashMap<u64, Sender<()>>>> = OnceLock::new();

fn view_notifiers() -> &'static Mutex<HashMap<u64, Sender<()>>> {
    VIEW_NOTIFIERS.get_or_init(|| Mutex::new(HashMap::new()))
}

struct ViewRegistration {
    view_id: u64,
}

impl ViewRegistration {
    fn new(view_id: u64, sender: Sender<()>) -> Result<Self, i32> {
        let mut notifiers = view_notifiers().lock().map_err(|_| -32)?;
        if notifiers.contains_key(&view_id) {
            return Err(-30);
        }
        notifiers.insert(view_id, sender);
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
    let Ok(notifiers) = view_notifiers().lock() else {
        return -32;
    };
    let Some(sender) = notifiers.get(&view_id) else {
        return -30;
    };

    match sender.try_send(()) {
        Ok(()) | Err(TrySendError::Full(())) => 0,
        Err(TrySendError::Closed(())) => -31,
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
            dirty: true,
            error: None,
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
            Ok(()) => self.has_snapshot = true,
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
    let (sender, receiver) = async_channel::bounded(1);
    let Ok(_registration) = ViewRegistration::new(view_id, sender) else {
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
            move |_, cx| create_managed_view(cx, view_id, callbacks, receiver),
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
    receiver: Receiver<()>,
) -> gpui::Entity<ManagedView> {
    let view = cx.new(|_| ManagedView::new(view_id, callbacks));
    let weak_view = view.downgrade();

    cx.spawn(async move |cx| {
        while receiver.recv().await.is_ok() {
            if weak_view
                .update(cx, |view, cx| view.invalidate(cx))
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
        let (sender, receiver) = async_channel::bounded(1);
        let registration = ViewRegistration::new(view_id, sender).unwrap();

        assert_eq!(notify(view_id), 0);
        assert_eq!(notify(view_id), 0);
        assert_eq!(receiver.try_recv(), Ok(()));
        assert!(receiver.try_recv().is_err());

        drop(registration);
        assert_eq!(notify(view_id), -30);
    }
}
