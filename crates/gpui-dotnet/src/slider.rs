use gpui::{
    AppContext, Axis, Bounds, Context, DragMoveEvent, ElementId, Empty, EntityId,
    InteractiveElement, IntoElement, MouseButton, MouseDownEvent, MouseUpEvent, ParentElement,
    Pixels, Point, Render, StatefulInteractiveElement, Styled, Window, canvas, div,
    prelude::FluentBuilder, px, relative, rgba,
};

use crate::{
    abi::{ManagedCallbacks, NativeControlEvent},
    resources::{ResourceCommand, SliderBindings, SliderConfiguration},
};

pub(crate) const EVENT_CHANGED: u16 = 4;
pub(crate) const EVENT_RELEASED: u16 = 5;

#[derive(Clone, Copy, Debug, PartialEq)]
pub(crate) enum SliderValue {
    Single(f32),
    Range(f32, f32),
}

impl SliderValue {
    fn start(self) -> f32 {
        match self {
            Self::Single(value) => value,
            Self::Range(start, _) => start,
        }
    }

    fn end(self) -> f32 {
        match self {
            Self::Single(value) => value,
            Self::Range(_, end) => end,
        }
    }

    fn is_range(self) -> bool {
        matches!(self, Self::Range(_, _))
    }

    fn clamp(self, min: f32, max: f32) -> Self {
        match self {
            Self::Single(value) => Self::Single(value.clamp(min, max)),
            Self::Range(start, end) => Self::Range(start.clamp(min, max), end.clamp(min, max)),
        }
    }

    fn set_start(&mut self, value: f32) {
        if let Self::Range(_, end) = self {
            *self = Self::Range(value.min(*end), *end);
        } else {
            *self = Self::Single(value);
        }
    }

    fn set_end(&mut self, value: f32) {
        if let Self::Range(start, _) = self {
            *self = Self::Range(*start, value.max(*start));
        } else {
            *self = Self::Single(value);
        }
    }
}

#[derive(Clone, Copy)]
struct SliderDrag((EntityId, bool));

impl Render for SliderDrag {
    fn render(&mut self, _: &mut Window, _: &mut Context<Self>) -> impl IntoElement {
        Empty
    }
}

pub(crate) struct ManagedSlider {
    session_id: u64,
    callbacks: ManagedCallbacks,
    min: f32,
    max: f32,
    step: f32,
    value: SliderValue,
    axis: Axis,
    disabled: bool,
    logarithmic: bool,
    bounds: Bounds<Pixels>,
    dragging: bool,
    bindings: SliderBindings,
    revision: u64,
    callback_error: Option<i32>,
}

impl ManagedSlider {
    pub(crate) fn new(
        session_id: u64,
        callbacks: ManagedCallbacks,
        configuration: &SliderConfiguration,
    ) -> Self {
        let value = configuration
            .initial_value
            .unwrap_or(SliderValue::Single(0.0))
            .clamp(configuration.min, configuration.max);
        Self {
            session_id,
            callbacks,
            min: configuration.min,
            max: configuration.max,
            step: configuration.step,
            value,
            axis: configuration.axis,
            disabled: configuration.disabled,
            logarithmic: configuration.logarithmic,
            bounds: Bounds::default(),
            dragging: false,
            bindings: configuration.bindings,
            revision: 0,
            callback_error: None,
        }
    }

    pub(crate) fn configure(
        &mut self,
        configuration: &SliderConfiguration,
        cx: &mut Context<Self>,
    ) {
        let changed = self.min != configuration.min
            || self.max != configuration.max
            || self.step != configuration.step
            || self.axis != configuration.axis
            || self.disabled != configuration.disabled
            || self.logarithmic != configuration.logarithmic
            || self.bindings.changed != configuration.bindings.changed
            || self.bindings.released != configuration.bindings.released;
        self.min = configuration.min;
        self.max = configuration.max;
        self.step = configuration.step;
        self.axis = configuration.axis;
        self.disabled = configuration.disabled;
        self.logarithmic = configuration.logarithmic;
        self.bindings = configuration.bindings;
        let value = self.value.clamp(self.min, self.max);
        if value != self.value {
            self.value = value;
        }
        if self.disabled {
            self.dragging = false;
        }
        if changed {
            cx.notify();
        }
    }

    pub(crate) fn apply_command(&mut self, command: &ResourceCommand, cx: &mut Context<Self>) {
        if command.command != 30 || self.disabled {
            return;
        }
        let start = f32::from_bits(command.a as u32);
        let end = f32::from_bits((command.a >> 32) as u32);
        let value = if command.b == 1 {
            SliderValue::Range(start, end)
        } else {
            SliderValue::Single(end)
        };
        if start.is_finite() && end.is_finite() {
            self.value = value.clamp(self.min, self.max);
            cx.notify();
        }
    }

    fn percentage_to_value(&self, percentage: f32) -> f32 {
        if self.logarithmic {
            let base = self.max / self.min;
            (base.powf(percentage) * self.min).clamp(self.min, self.max)
        } else {
            self.min + (self.max - self.min) * percentage
        }
    }

    fn value_to_percentage(&self, value: f32) -> f32 {
        if self.logarithmic {
            let base = self.max / self.min;
            (value / self.min).log(base).clamp(0.0, 1.0)
        } else {
            let range = self.max - self.min;
            if range <= 0.0 {
                0.0
            } else {
                ((value - self.min) / range).clamp(0.0, 1.0)
            }
        }
    }

    fn percentages(&self) -> (f32, f32) {
        match self.value {
            SliderValue::Single(value) => (0.0, self.value_to_percentage(value)),
            SliderValue::Range(start, end) => (
                self.value_to_percentage(start),
                self.value_to_percentage(end),
            ),
        }
    }

    fn update_value_by_position(
        &mut self,
        position: Point<Pixels>,
        is_start: bool,
        cx: &mut Context<Self>,
    ) {
        let total_size = if self.axis == Axis::Horizontal {
            self.bounds.size.width
        } else {
            self.bounds.size.height
        };
        if total_size <= px(0.) {
            return;
        }

        self.dragging = true;
        let inner_position = if self.axis == Axis::Horizontal {
            position.x - self.bounds.left()
        } else {
            self.bounds.bottom() - position.y
        };
        let percentage = (inner_position.clamp(px(0.), total_size) / total_size).clamp(0.0, 1.0);
        let (current_start, current_end) = self.percentages();
        let percentage = if is_start {
            percentage.min(current_end)
        } else {
            percentage.max(current_start)
        };
        let value = self.min
            + ((self.percentage_to_value(percentage) - self.min) / self.step).round() * self.step;
        let previous = self.value;
        if is_start {
            self.value.set_start(value.clamp(self.min, self.max));
        } else {
            self.value.set_end(value.clamp(self.min, self.max));
        }
        if self.value != previous {
            self.revision = self.revision.wrapping_add(1).max(1);
            self.emit(self.bindings.changed, EVENT_CHANGED, cx);
            cx.notify();
        }
    }

    fn handle_release(&mut self, cx: &mut Context<Self>) {
        if !self.dragging {
            return;
        }
        self.dragging = false;
        self.emit(self.bindings.released, EVENT_RELEASED, cx);
    }

    fn on_track_mouse_down(
        &mut self,
        event: &MouseDownEvent,
        _: &mut Window,
        cx: &mut Context<Self>,
    ) {
        let (start, end) = self.percentages();
        let is_start = if self.value.is_range() {
            let total_size = if self.axis == Axis::Horizontal {
                self.bounds.size.width
            } else {
                self.bounds.size.height
            };
            if total_size <= px(0.) {
                return;
            }
            let position = if self.axis == Axis::Horizontal {
                event.position.x - self.bounds.left()
            } else {
                self.bounds.bottom() - event.position.y
            };
            position < ((start + end) / 2.0) * total_size
        } else {
            false
        };
        self.update_value_by_position(event.position, is_start, cx);
    }

    fn on_drag_move(
        &mut self,
        event: &DragMoveEvent<SliderDrag>,
        _: &mut Window,
        cx: &mut Context<Self>,
    ) {
        let SliderDrag((resource_id, is_start)) = event.drag(cx);
        if *resource_id == cx.entity().entity_id() {
            self.update_value_by_position(event.event.position, *is_start, cx);
        }
    }

    fn on_mouse_up(&mut self, _: &MouseUpEvent, _: &mut Window, cx: &mut Context<Self>) {
        self.handle_release(cx);
    }

    fn emit(&mut self, token: u64, kind: u16, cx: &mut Context<Self>) {
        if token == 0 {
            return;
        }
        let callback = self
            .callbacks
            .control_event
            .expect("callbacks were validated before application startup");
        let mut data = [0u8; 8];
        data[..4].copy_from_slice(&self.value.start().to_le_bytes());
        let data_length = if self.value.is_range() {
            data[4..].copy_from_slice(&self.value.end().to_le_bytes());
            8
        } else {
            4
        };
        let event = NativeControlEvent {
            kind,
            flags: if self.value.is_range() { 2 } else { 0 },
            reserved: 0,
            revision: self.revision,
            data: data.as_ptr(),
            data_length,
            reserved2: 0,
        };
        let status = unsafe { callback(self.session_id, token, &event) };
        if status != 0 {
            self.callback_error = Some(status);
            cx.notify();
        }
    }
}

impl Render for ManagedSlider {
    fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        if let Some(status) = self.callback_error {
            return div()
                .size_full()
                .flex()
                .items_center()
                .text_color(rgba(0xB42318FF))
                .child(format!("Managed slider event failed with status {status}."))
                .into_any_element();
        }

        let entity_id = cx.entity().entity_id();
        let (start, end) = self.percentages();
        let is_range = self.value.is_range();
        let axis = self.axis;
        let disabled = self.disabled;
        let slider = cx.entity();

        let mut track = div()
            .id(("managed-slider-track", entity_id))
            .relative()
            .flex()
            .items_center()
            .when(axis == Axis::Horizontal, |this| this.w_full().h(px(24.)))
            .when(axis == Axis::Vertical, |this| this.h_full().w(px(24.)))
            .child(
                div()
                    .absolute()
                    .when(axis == Axis::Horizontal, |this| {
                        this.left(px(0.)).right(px(0.)).top(px(9.)).h(px(6.))
                    })
                    .when(axis == Axis::Vertical, |this| {
                        this.top(px(0.)).bottom(px(0.)).left(px(9.)).w(px(6.))
                    })
                    .rounded_full()
                    .bg(rgba(0xCBD5E1FF)),
            )
            .child(
                div()
                    .absolute()
                    .when(axis == Axis::Horizontal, |this| {
                        this.left(relative(start))
                            .right(relative(1. - end))
                            .top(px(9.))
                            .h(px(6.))
                    })
                    .when(axis == Axis::Vertical, |this| {
                        this.bottom(relative(start))
                            .top(relative(1. - end))
                            .left(px(9.))
                            .w(px(6.))
                    })
                    .rounded_full()
                    .bg(rgba(0x2563EBFF)),
            );

        let bounds_owner = slider.clone();
        track = track.child(
            canvas(
                move |bounds, _, cx| {
                    bounds_owner.update(cx, |slider, _| slider.bounds = bounds);
                },
                |_, _, _, _| {},
            )
            .absolute()
            .size_full(),
        );

        if !disabled {
            track = track
                .on_mouse_down(MouseButton::Left, cx.listener(Self::on_track_mouse_down))
                .when(!is_range, |this| {
                    this.on_drag(SliderDrag((entity_id, false)), |drag, _, _, cx| {
                        cx.stop_propagation();
                        cx.new(|_| drag.clone())
                    })
                    .on_drag_move(cx.listener(Self::on_drag_move))
                });

            let thumb = |position: f32, is_start: bool| {
                let mut thumb = div()
                    .id((
                        ElementId::from(("managed-slider-thumb", entity_id)),
                        if is_start { "start" } else { "end" },
                    ))
                    .absolute()
                    .flex()
                    .items_center()
                    .justify_center()
                    .w(px(16.))
                    .h(px(16.))
                    .rounded_full()
                    .bg(rgba(0xFFFFFFFF))
                    .border(px(1.))
                    .border_color(rgba(0x2563EBFF));
                thumb = if axis == Axis::Horizontal {
                    thumb.left(relative(position)).top(px(4.)).ml(-px(8.))
                } else {
                    thumb.bottom(relative(position)).left(px(4.)).mb(-px(8.))
                };
                thumb
                    .on_mouse_down(MouseButton::Left, |_, _, cx| cx.stop_propagation())
                    .on_drag(SliderDrag((entity_id, is_start)), |drag, _, _, cx| {
                        cx.stop_propagation();
                        cx.new(|_| drag.clone())
                    })
                    .on_drag_move(cx.listener(Self::on_drag_move))
            };

            if is_range {
                track = track.child(thumb(start, true));
            }
            track = track.child(thumb(end, false));
        }

        let root = div()
            .id(("managed-slider", entity_id))
            .size_full()
            .min_w_0()
            .relative()
            .flex()
            .when(axis == Axis::Horizontal, |this| this.items_center())
            .when(axis == Axis::Vertical, |this| this.justify_center())
            .when(!disabled, |this| {
                this.on_mouse_up(MouseButton::Left, cx.listener(Self::on_mouse_up))
                    .on_mouse_up_out(MouseButton::Left, cx.listener(Self::on_mouse_up))
            })
            .child(track);
        root.into_any_element()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn slider_values_clamp_without_changing_their_shape() {
        assert_eq!(
            SliderValue::Single(-1.).clamp(0., 10.),
            SliderValue::Single(0.)
        );
        assert_eq!(
            SliderValue::Range(-1., 12.).clamp(0., 10.),
            SliderValue::Range(0., 10.)
        );
    }

    #[test]
    fn linear_percentage_mapping_is_stable() {
        let configuration = SliderConfiguration {
            key: crate::resources::ResourceKey::new(1, gpui::SharedString::new("slider")),
            min: 10.,
            max: 110.,
            step: 1.,
            initial_value: Some(SliderValue::Single(60.)),
            axis: Axis::Horizontal,
            disabled: false,
            logarithmic: false,
            bindings: SliderBindings::default(),
        };
        let slider = ManagedSlider::new(1, callbacks(), &configuration);
        assert_eq!(slider.percentages(), (0., 0.5));
    }

    fn callbacks() -> ManagedCallbacks {
        ManagedCallbacks {
            struct_size: 0,
            render: None,
            click: None,
            list_render_range: None,
            control_event: None,
            application_started: None,
            window_closed: None,
        }
    }
}
