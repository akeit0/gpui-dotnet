use std::{
    cell::RefCell,
    rc::{Rc, Weak},
    sync::atomic::{AtomicU64, Ordering},
};

use gpui::{
    AnyElement, App, AppContext, Bounds, Element, ElementId, Entity, IntoElement, Pixels, Point,
    Window,
};
use gpui_base::PopoverState;

use crate::{abi::NativeControlEvent, collections::CollectionEngine};

struct Request {
    id: u64,
    owner: u32,
    source: Weak<RefCell<CollectionEngine>>,
    index: usize,
    artifact: u64,
    item_id: u64,
    bounds: Bounds<Pixels>,
    mask: Bounds<Pixels>,
    position: Point<Pixels>,
    seen: bool,
    declared: bool,
    state: Option<Entity<PopoverState>>,
}

/// One window-owned request, independent of row element and managed artifact lifetimes.
/// The anchor keeps scalar identities and a weak source reference, plus window-owned focus state.
#[derive(Default)]
pub(crate) struct RowMenus {
    request: RefCell<Option<Request>>,
}

impl RowMenus {
    pub(crate) fn begin_frame(&self, window: &mut Window, cx: &mut App) {
        let closed = self.request.borrow().as_ref().is_some_and(|r| {
            r.state
                .as_ref()
                .is_some_and(|state| !state.read(cx).is_open())
        });
        if closed {
            self.dismiss(window, cx);
        }
        if let Some(request) = self.request.borrow_mut().as_mut() {
            request.seen = false;
            request.declared = false;
        }
    }

    pub(crate) fn finish_declarations(&self, window: &mut Window, cx: &mut App) {
        if self.request.borrow().as_ref().is_some_and(|r| !r.declared) {
            self.dismiss(window, cx);
        }
    }

    pub(crate) fn observe(
        &self,
        source: &Rc<RefCell<CollectionEngine>>,
        index: usize,
        bounds: Bounds<Pixels>,
        mask: Bounds<Pixels>,
    ) {
        if let Some(request) = self.request.borrow_mut().as_mut()
            && request.source.ptr_eq(&Rc::downgrade(source))
            && request.index == index
        {
            request.seen = request.bounds == bounds && request.mask == mask;
        }
    }

    #[allow(clippy::too_many_arguments)]
    pub(crate) fn open(
        &self,
        source: &Rc<RefCell<CollectionEngine>>,
        index: usize,
        token: u64,
        bounds: Bounds<Pixels>,
        mask: Bounds<Pixels>,
        position: Point<Pixels>,
        window: &mut Window,
        cx: &mut App,
    ) -> bool {
        let Some((artifact, events)) = source.borrow().cached_identified_row(index) else {
            return false;
        };
        self.dismiss(window, cx);
        static NEXT: AtomicU64 = AtomicU64::new(1);
        let id = NEXT
            .try_update(Ordering::Relaxed, Ordering::Relaxed, |id| id.checked_add(1))
            .expect("row menu identity exhausted");
        *self.request.borrow_mut() = Some(Request {
            id,
            owner: (token >> 32) as u32,
            source: Rc::downgrade(source),
            index,
            artifact,
            item_id: events.item_id.unwrap(),
            bounds,
            mask,
            position,
            seen: true,
            declared: false,
            state: None,
        });
        let mut data = [0u8; 24];
        data[..4].copy_from_slice(&events.index.to_le_bytes());
        data[8..16].copy_from_slice(&events.item_id.unwrap().to_le_bytes());
        data[16..].copy_from_slice(&id.to_le_bytes());
        let event = NativeControlEvent {
            kind: crate::semantic::EVENT_LIST_CONTEXT_MENU_REQUESTED,
            flags: u16::from(events.content_revision.is_some()) << 1,
            revision: events.content_revision.unwrap_or(0),
            data: data.as_ptr(),
            data_length: 24,
            reserved: 0,
            reserved2: 0,
        };
        let callback = events.callbacks.control_event.expect("validated callbacks");
        let status = unsafe { callback(events.session_id, token, &event) };
        crate::app_host::after_detached_callback(events.session_id, status);
        window.refresh();
        true
    }

    pub(crate) fn bind(
        &self,
        id: u64,
        owner: u32,
        window: &mut Window,
        cx: &mut App,
    ) -> Option<(Entity<PopoverState>, Point<Pixels>)> {
        let mut current = self.request.borrow_mut();
        let request = current
            .as_mut()
            .filter(|r| r.id == id && r.owner == owner && !r.declared)?;
        request.declared = true;
        let state = request.state.get_or_insert_with(|| {
            let state = cx.new(|cx| PopoverState::new(false, cx));
            state.update(cx, |state, cx| state.show(window, cx));
            state
        });
        Some((state.clone(), request.position))
    }

    fn valid(&self, id: u64) -> bool {
        let current = self.request.borrow();
        let Some(request) = current.as_ref().filter(|r| r.id == id && r.seen) else {
            return false;
        };
        let Some(source) = request.source.upgrade() else {
            return false;
        };
        let source = source.borrow();
        source
            .cached_identified_row(request.index)
            .is_some_and(|(artifact, events)| {
                artifact == request.artifact && events.item_id == Some(request.item_id)
            })
    }

    fn dismiss(&self, window: &mut Window, cx: &mut App) {
        let previous = self.request.borrow_mut().take();
        if let Some(state) = previous.and_then(|r| r.state) {
            state.update(cx, |state, cx| state.dismiss(window, cx));
        }
    }
}

/// Deferred prepaint runs after normal rows. Reject the entire layer, including its hitboxes
/// and callbacks, when the current frame did not paint the original anchor in the same place.
pub(crate) struct Guard {
    pub(crate) child: AnyElement,
    pub(crate) menus: Rc<RowMenus>,
    pub(crate) id: u64,
    pub(crate) stack: Rc<crate::overlay::OverlayStack>,
    pub(crate) token: crate::overlay::OverlayToken,
}

impl IntoElement for Guard {
    type Element = Self;
    fn into_element(self) -> Self {
        self
    }
}

impl Element for Guard {
    type RequestLayoutState = ();
    type PrepaintState = bool;
    fn id(&self) -> Option<ElementId> {
        None
    }
    fn source_location(&self) -> Option<&'static core::panic::Location<'static>> {
        None
    }
    fn request_layout(
        &mut self,
        _: Option<&gpui::GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        window: &mut Window,
        cx: &mut App,
    ) -> (gpui::LayoutId, ()) {
        (self.child.request_layout(window, cx), ())
    }
    fn prepaint(
        &mut self,
        _: Option<&gpui::GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        _: Bounds<Pixels>,
        _: &mut (),
        window: &mut Window,
        cx: &mut App,
    ) -> bool {
        if !self.menus.valid(self.id) {
            self.stack.set_captures_input(&self.token, false);
            self.menus.dismiss(window, cx);
            return false;
        }
        self.child.prepaint(window, cx);
        true
    }
    fn paint(
        &mut self,
        _: Option<&gpui::GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        _: Bounds<Pixels>,
        _: &mut (),
        visible: &mut bool,
        window: &mut Window,
        cx: &mut App,
    ) {
        if *visible {
            self.child.paint(window, cx);
        }
    }
}
