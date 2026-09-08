use std::{
    cell::RefCell,
    rc::{Rc, Weak},
    sync::atomic::{AtomicU64, Ordering},
    time::Duration,
};

use gpui::{
    AnyElement, App, Bounds, Element, ElementId, Hitbox, InteractiveElement, IntoElement,
    MouseDownEvent, MouseMoveEvent, ParentElement, Pixels, ScrollWheelEvent,
    StatefulInteractiveElement, Styled, Window, anchored, canvas, deferred, div, point, px,
};
use gpui_base::Positioner;

use crate::{abi::NativeControlEvent, resources::ManagedListResource};

struct Request {
    id: u64,
    source: Weak<RefCell<ManagedListResource>>,
    index: usize,
    target: u32,
    artifact: u64,
    item_id: u64,
    owner: u32,
    hitbox: Hitbox,
    seen: bool,
    requested: bool,
    declared: bool,
    trigger_hovered: bool,
    content_hovered: bool,
    transition: u64,
    configuration: crate::tooltip::TooltipConfiguration,
}

impl Request {
    fn matches(
        &self,
        source: &Rc<RefCell<ManagedListResource>>,
        index: usize,
        target: u32,
    ) -> bool {
        self.source.ptr_eq(&Rc::downgrade(source)) && self.index == index && self.target == target
    }

    fn valid(&self) -> bool {
        self.seen
            && self.source.upgrade().is_some_and(|source| {
                let source = source.borrow();
                source.tooltip_token != 0
                    && source.tooltip == self.configuration
                    && source
                        .cached_identified_row(self.index)
                        .is_some_and(|(artifact, row)| {
                            artifact == self.artifact && row.item_id == Some(self.item_id)
                        })
            })
    }
}

/// A single delayed hover request per window. Timers retain only a weak coordinator reference;
/// the request retains scalar row identity and a weak collection reference, never a row batch.
#[derive(Default)]
pub(crate) struct RowTooltips {
    request: RefCell<Option<Request>>,
}

impl RowTooltips {
    pub(crate) fn begin_frame(&self) {
        if let Some(request) = self.request.borrow_mut().as_mut() {
            request.seen = false;
            request.declared = false;
        }
    }

    pub(crate) fn finish_declarations(&self, window: &mut Window) {
        if self
            .request
            .borrow()
            .as_ref()
            .is_some_and(|r| r.requested && !r.declared)
        {
            self.dismiss(window);
        }
    }

    pub(crate) fn dismiss(&self, window: &mut Window) {
        if self
            .request
            .borrow_mut()
            .take()
            .is_some_and(|r| r.requested)
        {
            window.refresh();
        }
    }

    fn observe(
        &self,
        source: &Rc<RefCell<ManagedListResource>>,
        index: usize,
        target: u32,
        hitbox: &Hitbox,
    ) {
        if let Some(request) = self.request.borrow_mut().as_mut()
            && request.matches(source, index, target)
            && request.hitbox.bounds == hitbox.bounds
            && request.hitbox.content_mask == hitbox.content_mask
        {
            request.hitbox = hitbox.clone();
            request.seen = true;
        }
    }

    fn hover(
        self: &Rc<Self>,
        source: &Rc<RefCell<ManagedListResource>>,
        index: usize,
        target: u32,
        hitbox: &Hitbox,
        hovered: bool,
        window: &mut Window,
        cx: &mut App,
    ) {
        let matches = self
            .request
            .borrow()
            .as_ref()
            .is_some_and(|r| r.matches(source, index, target));
        if !hovered {
            if matches {
                self.change_hover(false, false, window, cx);
            }
            return;
        }
        if matches {
            self.change_hover(false, true, window, cx);
            return;
        }
        let (artifact, row, configuration, token) = {
            let source = source.borrow();
            if source.tooltip_token == 0 {
                return;
            }
            let Some((artifact, row)) = source.cached_identified_row(index) else {
                return;
            };
            (artifact, row, source.tooltip, source.tooltip_token)
        };
        self.dismiss(window);
        static NEXT: AtomicU64 = AtomicU64::new(1);
        let id = NEXT
            .try_update(Ordering::Relaxed, Ordering::Relaxed, |id| id.checked_add(1))
            .expect("row tooltip identity exhausted");
        *self.request.borrow_mut() = Some(Request {
            id,
            source: Rc::downgrade(source),
            index,
            target,
            artifact,
            item_id: row.item_id.unwrap(),
            owner: (token >> 32) as u32,
            hitbox: hitbox.clone(),
            seen: true,
            requested: false,
            declared: false,
            trigger_hovered: true,
            content_hovered: false,
            transition: 0,
            configuration,
        });
        let weak = Rc::downgrade(self);
        window
            .spawn(cx, async move |cx| {
                cx.background_executor()
                    .timer(Duration::from_millis(configuration.show_delay_ms))
                    .await;
                cx.update(|window, cx| {
                    if let Some(tooltips) = weak.upgrade() {
                        tooltips.show(id, window, cx);
                    }
                })
                .ok();
            })
            .detach();
    }

    fn show(&self, id: u64, window: &mut Window, _cx: &mut App) {
        let packet = {
            let mut current = self.request.borrow_mut();
            let Some(request) = current.as_mut().filter(|r| r.id == id && !r.requested) else {
                return;
            };
            if !request.valid() || !request.trigger_hovered || !request.hitbox.is_hovered(window) {
                *current = None;
                return;
            }
            let source = request.source.upgrade().unwrap();
            let source = source.borrow();
            let (_, row) = source.cached_identified_row(request.index).unwrap();
            let token = source.tooltip_token;
            request.owner = (token >> 32) as u32;
            request.requested = true;
            (row, token)
        };
        let (row, token) = packet;
        let mut data = [0u8; 24];
        data[..4].copy_from_slice(&row.index.to_le_bytes());
        data[8..16].copy_from_slice(&row.item_id.unwrap().to_le_bytes());
        data[16..].copy_from_slice(&id.to_le_bytes());
        let event = NativeControlEvent {
            kind: crate::semantic::EVENT_LIST_TOOLTIP_REQUESTED,
            flags: u16::from(row.content_revision.is_some()) << 1,
            revision: row.content_revision.unwrap_or(0),
            data: data.as_ptr(),
            data_length: 24,
            reserved: 0,
            reserved2: 0,
        };
        let callback = row.callbacks.control_event.expect("validated callbacks");
        let status = unsafe { callback(row.session_id, token, &event) };
        crate::app_host::after_detached_callback(row.session_id, status);
        window.refresh();
    }

    fn change_hover(
        self: &Rc<Self>,
        content: bool,
        hovered: bool,
        window: &mut Window,
        cx: &mut App,
    ) {
        let hide = {
            let mut current = self.request.borrow_mut();
            let Some(request) = current.as_mut() else {
                return;
            };
            let field = if content {
                &mut request.content_hovered
            } else {
                &mut request.trigger_hovered
            };
            if *field == hovered {
                return;
            }
            *field = hovered;
            request.transition = request
                .transition
                .checked_add(1)
                .expect("tooltip transition exhausted");
            if request.trigger_hovered || request.content_hovered {
                return;
            }
            if !request.requested {
                *current = None;
                return;
            }
            (
                request.id,
                request.transition,
                request.configuration.hide_delay_ms,
            )
        };
        let weak = Rc::downgrade(self);
        window
            .spawn(cx, async move |cx| {
                cx.background_executor()
                    .timer(Duration::from_millis(hide.2))
                    .await;
                cx.update(|window, _| {
                    if let Some(tooltips) = weak.upgrade()
                        && tooltips
                            .request
                            .borrow()
                            .as_ref()
                            .is_some_and(|r| (r.id, r.transition) == (hide.0, hide.1))
                    {
                        tooltips.dismiss(window);
                    }
                })
                .ok();
            })
            .detach();
    }

    fn content_hover(self: &Rc<Self>, id: u64, hovered: bool, window: &mut Window, cx: &mut App) {
        if self.request.borrow().as_ref().is_some_and(|r| r.id == id) {
            self.change_hover(true, hovered, window, cx);
        }
    }

    fn bind(
        &self,
        id: u64,
        owner: u32,
    ) -> Option<(Bounds<Pixels>, crate::tooltip::TooltipConfiguration)> {
        let mut current = self.request.borrow_mut();
        let request = current
            .as_mut()
            .filter(|r| r.id == id && r.owner == owner && r.requested && !r.declared)?;
        request.declared = true;
        Some((request.hitbox.bounds, request.configuration))
    }

    fn valid(&self, id: u64) -> bool {
        self.request
            .borrow()
            .as_ref()
            .is_some_and(|r| r.id == id && r.valid())
    }
}

/// Completes anchor observation after all normal rows, and dismisses without consuming input.
pub(crate) fn frame_end(tooltips: Rc<RowTooltips>) -> AnyElement {
    let paint_tooltips = tooltips.clone();
    canvas(
        move |_, window, _| {
            if tooltips
                .request
                .borrow()
                .as_ref()
                .is_some_and(|r| !r.valid())
            {
                tooltips.dismiss(window);
            }
        },
        move |_, _, window, _| {
            let press = paint_tooltips.clone();
            window.on_mouse_event(move |_: &MouseDownEvent, phase, window, _| {
                if phase.capture() {
                    press.dismiss(window);
                }
            });
            let wheel = paint_tooltips.clone();
            window.on_mouse_event(move |_: &ScrollWheelEvent, phase, window, _| {
                if phase.capture() {
                    wheel.dismiss(window);
                }
            });
        },
    )
    .into_any_element()
}

pub(crate) struct Target {
    pub(crate) child: AnyElement,
    pub(crate) source: Rc<RefCell<ManagedListResource>>,
    pub(crate) index: usize,
    pub(crate) target: u32,
    pub(crate) tooltips: Rc<RowTooltips>,
}

impl IntoElement for Target {
    type Element = Self;
    fn into_element(self) -> Self {
        self
    }
}

impl Element for Target {
    type RequestLayoutState = ();
    type PrepaintState = Hitbox;
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
        bounds: Bounds<Pixels>,
        _: &mut (),
        window: &mut Window,
        cx: &mut App,
    ) -> Hitbox {
        let hitbox = window.insert_hitbox(bounds, gpui::HitboxBehavior::Normal);
        self.tooltips
            .observe(&self.source, self.index, self.target, &hitbox);
        self.child.prepaint(window, cx);
        hitbox
    }
    fn paint(
        &mut self,
        _: Option<&gpui::GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        _: Bounds<Pixels>,
        _: &mut (),
        hitbox: &mut Hitbox,
        window: &mut Window,
        cx: &mut App,
    ) {
        let hitbox = hitbox.clone();
        let source = self.source.clone();
        let tooltips = self.tooltips.clone();
        let index = self.index;
        let target = self.target;
        window.on_mouse_event(move |event: &MouseMoveEvent, phase, window, cx| {
            if phase.bubble() {
                tooltips.hover(
                    &source,
                    index,
                    target,
                    &hitbox,
                    event.pressed_button.is_none() && hitbox.is_hovered(window),
                    window,
                    cx,
                );
            }
        });
        self.child.paint(window, cx);
    }
}

pub(crate) fn tooltip(
    tooltips: Rc<RowTooltips>,
    id: u64,
    owner: u32,
    content: AnyElement,
    _window: &mut Window,
    _cx: &mut App,
) -> AnyElement {
    let Some((bounds, configuration)) = tooltips.bind(id, owner) else {
        return div().absolute().into_any_element();
    };
    let hover = tooltips.clone();
    let content = div()
        .id(("row-tooltip-content", id))
        .occlude()
        .on_hover(move |hovered, window, cx| hover.content_hover(id, *hovered, window, cx))
        .child(content);
    let popup = Positioner::side(bounds)
        .placement(crate::tooltip::foundation_placement(
            configuration.placement,
        ))
        .align(crate::tooltip::foundation_alignment(
            configuration.alignment,
        ))
        .offset(px(configuration.gap))
        .margin(px(configuration.margin))
        .child(content);
    let child = anchored()
        .position(point(px(0.), px(0.)))
        .child(popup)
        .into_any_element();
    div()
        .absolute()
        .child(
            deferred(Layer {
                child,
                tooltips,
                id,
            })
            .with_priority(200),
        )
        .into_any_element()
}

struct Layer {
    child: AnyElement,
    tooltips: Rc<RowTooltips>,
    id: u64,
}
impl IntoElement for Layer {
    type Element = Self;
    fn into_element(self) -> Self {
        self
    }
}
impl Element for Layer {
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
        if !self.tooltips.valid(self.id) {
            self.tooltips.dismiss(window);
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
