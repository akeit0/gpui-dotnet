use gpui::{
    App, AppContext, Axis, Bounds, Context, DragMoveEvent, ElementId, Empty, EntityId, FocusHandle,
    Focusable, InteractiveElement, IntoElement, KeyDownEvent, KeyUpEvent, MouseButton,
    MouseDownEvent, MouseUpEvent, Orientation, ParentElement, Pixels, Point, Render, Role,
    StatefulInteractiveElement, Styled, Window, canvas, div, prelude::FluentBuilder, px, relative,
    rgba,
};

use crate::{
    abi::{ManagedCallbacks, NativeControlEvent},
    resources::{ResourceCommand, SliderBindings, SliderConfiguration},
    semantic::{COMMAND_SLIDER_SET_VALUE, EVENT_SLIDER_CHANGED, EVENT_SLIDER_RELEASED},
    theme::SharedTheme,
};

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
    focus_handle: FocusHandle,
    min: f32,
    max: f32,
    step: f32,
    value: SliderValue,
    axis: Axis,
    disabled: bool,
    logarithmic: bool,
    bounds: Bounds<Pixels>,
    dragging: bool,
    active_thumb_is_start: bool,
    keyboard_active: bool,
    bindings: SliderBindings,
    revision: u64,
    callback_error: Option<i32>,
    theme: SharedTheme,
}

impl ManagedSlider {
    pub(crate) fn new(
        session_id: u64,
        callbacks: ManagedCallbacks,
        configuration: &SliderConfiguration,
        theme: SharedTheme,
        cx: &mut Context<Self>,
    ) -> Self {
        let value = configuration
            .initial_value
            .unwrap_or(SliderValue::Single(0.0))
            .clamp(configuration.min, configuration.max);
        Self {
            session_id,
            callbacks,
            focus_handle: cx.focus_handle().tab_stop(!configuration.disabled),
            min: configuration.min,
            max: configuration.max,
            step: configuration.step,
            value,
            axis: configuration.axis,
            disabled: configuration.disabled,
            logarithmic: configuration.logarithmic,
            bounds: Bounds::default(),
            dragging: false,
            active_thumb_is_start: false,
            keyboard_active: false,
            bindings: configuration.bindings,
            revision: 0,
            callback_error: None,
            theme,
        }
    }

    pub(crate) fn configure(
        &mut self,
        configuration: &SliderConfiguration,
        cx: &mut Context<Self>,
    ) {
        let disabled_changed = self.disabled != configuration.disabled;
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
        if disabled_changed {
            self.focus_handle = self.focus_handle.clone().tab_stop(!self.disabled);
        }
        let value = self.value.clamp(self.min, self.max);
        if value != self.value {
            self.value = value;
        }
        if self.disabled {
            self.dragging = false;
            self.keyboard_active = false;
        }
        if changed {
            cx.notify();
        }
    }

    pub(crate) fn apply_command(&mut self, command: &ResourceCommand, cx: &mut Context<Self>) {
        if command.command != COMMAND_SLIDER_SET_VALUE || self.disabled {
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
        self.update_value(value, is_start, cx);
    }

    fn update_value(&mut self, value: f32, is_start: bool, cx: &mut Context<Self>) {
        let previous = self.value;
        if is_start {
            self.value.set_start(value.clamp(self.min, self.max));
        } else {
            self.value.set_end(value.clamp(self.min, self.max));
        }
        if self.value != previous {
            self.revision = self.revision.wrapping_add(1).max(1);
            self.emit(self.bindings.changed, EVENT_SLIDER_CHANGED, cx);
            cx.notify();
        }
    }

    fn handle_release(&mut self, cx: &mut Context<Self>) {
        if !self.dragging {
            return;
        }
        self.dragging = false;
        self.emit(self.bindings.released, EVENT_SLIDER_RELEASED, cx);
    }

    fn on_key_down(&mut self, event: &KeyDownEvent, _: &mut Window, cx: &mut Context<Self>) {
        if self.disabled || !is_slider_key(&event.keystroke.key) {
            return;
        }

        let is_start = self.value.is_range() && self.active_thumb_is_start;
        let current = if is_start {
            self.value.start()
        } else {
            self.value.end()
        };
        let next = match event.keystroke.key.as_str() {
            "home" => self.min,
            "end" => self.max,
            "left" if self.axis == Axis::Horizontal => current - self.step,
            "right" if self.axis == Axis::Horizontal => current + self.step,
            "up" if self.axis == Axis::Vertical => current + self.step,
            "down" if self.axis == Axis::Vertical => current - self.step,
            "pageup" => current + self.step * 10.,
            "pagedown" => current - self.step * 10.,
            _ => return,
        };

        self.keyboard_active = true;
        self.dragging = false;
        self.update_value(next, is_start, cx);
        cx.stop_propagation();
    }

    fn on_key_up(&mut self, event: &KeyUpEvent, _: &mut Window, cx: &mut Context<Self>) {
        if !self.keyboard_active || !is_slider_key(&event.keystroke.key) {
            return;
        }
        self.keyboard_active = false;
        self.emit(self.bindings.released, EVENT_SLIDER_RELEASED, cx);
        cx.stop_propagation();
    }

    fn on_track_mouse_down(
        &mut self,
        event: &MouseDownEvent,
        window: &mut Window,
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
        self.active_thumb_is_start = is_start;
        self.keyboard_active = false;
        self.focus_handle.focus(window, cx);
        self.update_value_by_position(event.position, is_start, cx);
    }

    fn on_thumb_mouse_down(
        &mut self,
        _: &MouseDownEvent,
        window: &mut Window,
        cx: &mut Context<Self>,
        is_start: bool,
    ) {
        self.active_thumb_is_start = is_start;
        self.keyboard_active = false;
        self.focus_handle.focus(window, cx);
        cx.stop_propagation();
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

fn is_slider_key(key: &str) -> bool {
    matches!(
        key,
        "home" | "end" | "left" | "right" | "up" | "down" | "pageup" | "pagedown"
    )
}

impl Focusable for ManagedSlider {
    fn focus_handle(&self, _: &App) -> FocusHandle {
        self.focus_handle.clone()
    }
}

impl Render for ManagedSlider {
    fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let theme = *self.theme.borrow();
        if let Some(status) = self.callback_error {
            return div()
                .size_full()
                .flex()
                .items_center()
                .text_color(rgba(theme.error))
                .child(format!("Managed slider event failed with status {status}."))
                .into_any_element();
        }

        let entity_id = cx.entity().entity_id();
        let (start, end) = self.percentages();
        let is_range = self.value.is_range();
        let axis = self.axis;
        let disabled = self.disabled;
        let value = self.value.end() as f64;
        let min = self.min as f64;
        let max = self.max as f64;
        let step = self.step as f64;
        let slider = cx.entity();
        let focus_handle = self.focus_handle.clone();

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
                    .bg(rgba(theme.border_variant)),
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
                    .bg(rgba(theme.accent)),
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
        track = track.debug_selector(|| "managed-slider-track".to_owned());

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
                    .bg(rgba(theme.surface_background))
                    .border(px(1.))
                    .border_color(rgba(theme.accent));
                thumb = if axis == Axis::Horizontal {
                    thumb.left(relative(position)).top(px(4.)).ml(-px(8.))
                } else {
                    thumb.bottom(relative(position)).left(px(4.)).mb(-px(8.))
                };
                thumb
                    .debug_selector(|| "managed-slider-thumb".to_owned())
                    .on_mouse_down(
                        MouseButton::Left,
                        cx.listener(move |slider, event, window, cx| {
                            slider.on_thumb_mouse_down(event, window, cx, is_start);
                        }),
                    )
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
            .id(&focus_handle)
            .role(Role::Slider)
            .aria_numeric_value(value)
            .aria_min_numeric_value(min)
            .aria_max_numeric_value(max)
            .aria_numeric_value_step(step)
            .aria_orientation(if axis == Axis::Vertical {
                Orientation::Vertical
            } else {
                Orientation::Horizontal
            })
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
            .track_focus(&focus_handle)
            .focus_visible(move |style| {
                style
                    .border(px(2.))
                    .border_color(rgba(theme.border_focused))
            })
            .key_context("GpuiDotnetSlider")
            .on_key_down(cx.listener(Self::on_key_down))
            .on_key_up(cx.listener(Self::on_key_up))
            .child(track);
        root.into_any_element()
    }
}

#[cfg(test)]
mod tests {
    use std::{cell::RefCell, rc::Rc};

    use gpui::{KeyUpEvent, Keystroke, Modifiers, TestAppContext, point, px, size};

    use super::*;

    fn theme() -> SharedTheme {
        Rc::new(RefCell::new(crate::theme::NativeTheme::default()))
    }

    thread_local! {
        static EVENTS: RefCell<Vec<(u16, u64, SliderValue)>> = const { RefCell::new(Vec::new()) };
    }

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

    #[gpui::test]
    fn linear_percentage_mapping_is_stable(cx: &mut TestAppContext) {
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
        let (slider, _) = cx.add_window_view(|_, cx| {
            ManagedSlider::new(1, callbacks(), &configuration, theme(), cx)
        });
        slider.update(cx, |slider, _| assert_eq!(slider.percentages(), (0., 0.5)));
    }

    #[gpui::test]
    fn track_click_emits_changed_and_release_events(cx: &mut TestAppContext) {
        clear_events();
        let configuration = configuration(SliderValue::Single(0.));
        let (_, cx) = cx.add_window_view(|_, cx| {
            ManagedSlider::new(7, callbacks(), &configuration, theme(), cx)
        });
        cx.simulate_resize(size(px(240.), px(80.)));
        cx.update(|window, _| window.refresh());

        let bounds = cx
            .debug_bounds("managed-slider-track")
            .expect("slider track should be laid out");
        let position = point(bounds.left() + bounds.size.width * 0.75, bounds.center().y);
        cx.simulate_mouse_down(position, MouseButton::Left, Modifiers::none());

        let recorded = events();
        assert_eq!(recorded.len(), 1);
        assert_eq!(recorded[0].0, EVENT_SLIDER_CHANGED);
        assert_eq!(recorded[0].2, SliderValue::Single(75.));

        cx.simulate_mouse_up(position, MouseButton::Left, Modifiers::none());
        let recorded = events();
        assert_eq!(recorded.len(), 2);
        assert_eq!(recorded[1].0, EVENT_SLIDER_RELEASED);
        assert_eq!(recorded[1].1, 1);
        assert_eq!(recorded[1].2, SliderValue::Single(75.));
    }

    #[gpui::test]
    fn range_track_click_moves_the_nearest_thumb_and_preserves_range_shape(
        cx: &mut TestAppContext,
    ) {
        clear_events();
        let configuration = configuration(SliderValue::Range(25., 75.));
        let (_, cx) = cx.add_window_view(|_, cx| {
            ManagedSlider::new(8, callbacks(), &configuration, theme(), cx)
        });
        cx.simulate_resize(size(px(240.), px(80.)));
        cx.update(|window, _| window.refresh());

        let bounds = cx
            .debug_bounds("managed-slider-track")
            .expect("slider track should be laid out");
        let position = point(bounds.left() + bounds.size.width * 0.1, bounds.center().y);
        cx.simulate_mouse_down(position, MouseButton::Left, Modifiers::none());
        cx.simulate_mouse_up(position, MouseButton::Left, Modifiers::none());

        let events = events();
        assert_eq!(events.len(), 2);
        assert_eq!(events[0].2, SliderValue::Range(10., 75.));
        assert_eq!(events[1].0, EVENT_SLIDER_RELEASED);
        assert_eq!(events[1].2, SliderValue::Range(10., 75.));
    }

    #[gpui::test]
    fn vertical_logarithmic_track_click_uses_the_native_axis_mapping(cx: &mut TestAppContext) {
        clear_events();
        let mut configuration = configuration(SliderValue::Single(1.));
        configuration.min = 1.;
        configuration.max = 100.;
        configuration.step = 1.;
        configuration.axis = Axis::Vertical;
        configuration.logarithmic = true;
        let (_, cx) = cx.add_window_view(|_, cx| {
            ManagedSlider::new(13, callbacks(), &configuration, theme(), cx)
        });
        cx.simulate_resize(size(px(80.), px(240.)));
        cx.update(|window, _| window.refresh());

        let bounds = cx
            .debug_bounds("managed-slider-track")
            .expect("slider track should be laid out");
        let position = point(bounds.center().x, bounds.top() + bounds.size.height * 0.5);
        cx.simulate_mouse_down(position, MouseButton::Left, Modifiers::none());
        cx.simulate_mouse_up(position, MouseButton::Left, Modifiers::none());

        let recorded = events();
        assert_eq!(recorded[0].2, SliderValue::Single(10.));
        assert_eq!(recorded[1].0, EVENT_SLIDER_RELEASED);
    }

    #[gpui::test]
    fn disabled_slider_ignores_pointer_input(cx: &mut TestAppContext) {
        clear_events();
        let mut configuration = configuration(SliderValue::Single(40.));
        configuration.disabled = true;
        let (_, cx) = cx.add_window_view(|_, cx| {
            ManagedSlider::new(9, callbacks(), &configuration, theme(), cx)
        });
        cx.simulate_resize(size(px(240.), px(80.)));
        cx.update(|window, _| window.refresh());

        let bounds = cx
            .debug_bounds("managed-slider-track")
            .expect("slider track should be laid out");
        let position = point(bounds.left() + bounds.size.width * 0.9, bounds.center().y);
        cx.simulate_mouse_down(position, MouseButton::Left, Modifiers::none());
        cx.simulate_mouse_up(position, MouseButton::Left, Modifiers::none());

        assert!(events().is_empty());
    }

    #[gpui::test]
    fn programmatic_set_value_does_not_emit_interaction_events(cx: &mut TestAppContext) {
        clear_events();
        let configuration = configuration(SliderValue::Single(10.));
        let (slider, _) = cx.add_window_view(|_, cx| {
            ManagedSlider::new(10, callbacks(), &configuration, theme(), cx)
        });
        let command = ResourceCommand {
            key: crate::resources::ResourceKey::new(1, gpui::SharedString::new("slider")),
            resource_kind: 4,
            command: 30,
            a: (20f32.to_bits() as u64) | ((20f32.to_bits() as u64) << 32),
            b: 0,
            data: gpui::SharedString::new(""),
        };
        slider.update(cx, |slider, cx| slider.apply_command(&command, cx));

        assert!(events().is_empty());
        slider.update(cx, |slider, _| {
            assert_eq!(slider.value, SliderValue::Single(20.))
        });
    }

    #[gpui::test]
    fn keyboard_step_emits_changed_then_released(cx: &mut TestAppContext) {
        clear_events();
        let configuration = configuration(SliderValue::Single(50.));
        let (slider, cx) = cx.add_window_view(|_, cx| {
            ManagedSlider::new(11, callbacks(), &configuration, theme(), cx)
        });
        cx.simulate_resize(size(px(240.), px(80.)));
        cx.update(|window, _| window.refresh());
        cx.update(|window, app| {
            slider.update(app, |slider, cx| slider.focus_handle.focus(window, cx));
        });

        cx.simulate_keystrokes("right");
        assert_eq!(
            events(),
            vec![(EVENT_SLIDER_CHANGED, 1, SliderValue::Single(55.))]
        );

        cx.simulate_event(KeyUpEvent {
            keystroke: Keystroke::parse("right").unwrap(),
        });
        assert_eq!(
            events(),
            vec![
                (EVENT_SLIDER_CHANGED, 1, SliderValue::Single(55.)),
                (EVENT_SLIDER_RELEASED, 1, SliderValue::Single(55.)),
            ]
        );
    }

    #[gpui::test]
    fn thumb_drag_emits_changed_and_released(cx: &mut TestAppContext) {
        clear_events();
        let configuration = configuration(SliderValue::Single(25.));
        let (_, cx) = cx.add_window_view(|_, cx| {
            ManagedSlider::new(12, callbacks(), &configuration, theme(), cx)
        });
        cx.simulate_resize(size(px(240.), px(80.)));
        cx.update(|window, _| window.refresh());

        let thumb = cx
            .debug_bounds("managed-slider-thumb")
            .expect("slider thumb should be laid out");
        let track = cx
            .debug_bounds("managed-slider-track")
            .expect("slider track should be laid out");
        let start = thumb.center();
        let end = point(track.left() + track.size.width * 0.75, start.y);
        cx.simulate_mouse_down(start, MouseButton::Left, Modifiers::none());
        cx.simulate_mouse_move(end, Some(MouseButton::Left), Modifiers::none());
        cx.simulate_mouse_move(end, Some(MouseButton::Left), Modifiers::none());
        cx.simulate_mouse_up(end, MouseButton::Left, Modifiers::none());

        let recorded = events();
        assert_eq!(
            recorded.last().copied(),
            Some((EVENT_SLIDER_RELEASED, 1, SliderValue::Single(75.)))
        );
        assert!(
            recorded
                .iter()
                .any(|event| event.0 == EVENT_SLIDER_CHANGED && event.2 == SliderValue::Single(75.))
        );
    }

    fn configuration(value: SliderValue) -> SliderConfiguration {
        SliderConfiguration {
            key: crate::resources::ResourceKey::new(1, gpui::SharedString::new("slider")),
            min: 0.,
            max: 100.,
            step: 5.,
            initial_value: Some(value),
            axis: Axis::Horizontal,
            disabled: false,
            logarithmic: false,
            bindings: SliderBindings {
                changed: 1,
                released: 2,
            },
        }
    }

    fn clear_events() {
        EVENTS.with(|events| events.borrow_mut().clear());
    }

    fn events() -> Vec<(u16, u64, SliderValue)> {
        EVENTS.with(|events| events.borrow().clone())
    }

    unsafe extern "C" fn capture_event(_: u64, _: u64, event: *const NativeControlEvent) -> i32 {
        let event = unsafe { &*event };
        let bytes = unsafe { std::slice::from_raw_parts(event.data, event.data_length as usize) };
        let start = f32::from_le_bytes(bytes[..4].try_into().unwrap());
        let value = if event.flags & 2 != 0 {
            let end = f32::from_le_bytes(bytes[4..8].try_into().unwrap());
            SliderValue::Range(start, end)
        } else {
            SliderValue::Single(start)
        };
        EVENTS.with(|events| {
            events
                .borrow_mut()
                .push((event.kind, event.revision, value))
        });
        0
    }

    fn callbacks() -> ManagedCallbacks {
        ManagedCallbacks {
            struct_size: 0,
            render: None,
            click: None,
            list_render_range: None,
            dynamic_frame: None,
            control_event: Some(capture_event),
            application_started: None,
            window_closed: None,
            menu_action: None,
        }
    }
}
