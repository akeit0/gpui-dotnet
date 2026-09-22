use std::time::Duration;

use gpui::{
    AnyElement, App, Context, ElementId, InteractiveElement, IntoElement, MouseButton,
    ParentElement, StatefulInteractiveElement, Styled, Window, div, px,
};
use gpui_base::{Align, Placement};

use crate::{
    app_host::ManagedView,
    resources::ResourceKey,
    side_popup::{SidePopupOptions, side_popup},
};

const TOOLTIP_PRIORITY: usize = 200;

#[derive(Clone, Copy, PartialEq)]
pub(crate) struct TooltipConfiguration {
    pub(crate) placement: u32,
    pub(crate) alignment: u32,
    pub(crate) show_delay_ms: u64,
    pub(crate) hide_delay_ms: u64,
    pub(crate) gap: f32,
    pub(crate) margin: f32,
}

impl Default for TooltipConfiguration {
    fn default() -> Self {
        Self {
            placement: 0,
            alignment: 1,
            show_delay_ms: 500,
            hide_delay_ms: 300,
            gap: 8.,
            margin: 8.,
        }
    }
}

impl TooltipConfiguration {
    pub(crate) fn from_snapshot(
        snapshot: &crate::snapshot::ValidatedSnapshot,
        node: &crate::snapshot::SnapshotNode,
    ) -> Self {
        use crate::semantic::*;
        let value = |code| {
            snapshot
                .ops(node)
                .iter()
                .rev()
                .find(|op| op.code == code)
                .map(|op| op.a)
        };
        let defaults = Self::default();
        Self {
            placement: value(OP_TOOLTIP_PLACEMENT).map_or(defaults.placement, |a| a as u32),
            alignment: value(OP_TOOLTIP_ALIGNMENT).map_or(defaults.alignment, |a| a as u32),
            show_delay_ms: value(OP_TOOLTIP_SHOW_DELAY_MS).unwrap_or(defaults.show_delay_ms),
            hide_delay_ms: value(OP_TOOLTIP_HIDE_DELAY_MS).unwrap_or(defaults.hide_delay_ms),
            gap: value(OP_TOOLTIP_GAP_PX).map_or(defaults.gap, |a| f32::from_bits(a as u32)),
            margin: value(OP_TOOLTIP_MARGIN_PX)
                .map_or(defaults.margin, |a| f32::from_bits(a as u32)),
        }
    }
}

#[derive(Default)]
struct TooltipState {
    visible: bool,
    trigger_hovered: bool,
    content_hovered: bool,
    generation: u64,
}

#[derive(Clone, Copy)]
enum HoverTarget {
    Trigger,
    Content,
}

#[derive(Clone, Copy)]
enum Transition {
    Show,
    Hide,
}

pub(crate) fn tooltip(
    key: ResourceKey,
    trigger: AnyElement,
    content: AnyElement,
    configuration: TooltipConfiguration,
    window: &mut Window,
    cx: &mut Context<ManagedView>,
) -> AnyElement {
    let tooltip_id: ElementId =
        gpui::SharedString::from(format!("managed-tooltip-{}-{}", key.owner_view, key.key)).into();
    let state = window.use_keyed_state(tooltip_id.clone(), cx, |_, _| TooltipState::default());

    let trigger_state = state.clone();
    let trigger_config = configuration;
    let pressed_state = state.clone();
    let trigger = div()
        .relative()
        .flex()
        .flex_none()
        .id((tooltip_id.clone(), "trigger"))
        .on_hover(move |hovered, window, cx| {
            update_hover(
                &trigger_state,
                HoverTarget::Trigger,
                *hovered,
                trigger_config,
                window,
                cx,
            );
        })
        .on_mouse_down(MouseButton::Left, move |_, window, cx| {
            hide_immediately(&pressed_state, window, cx);
        })
        .child(trigger);
    let content = if state.read(cx).visible {
        let content_state = state.clone();
        let content_config = configuration;
        let pressed_state = state.clone();
        Some(
            div()
                .id((tooltip_id.clone(), "content"))
                .occlude()
                .on_hover(move |hovered, window, cx| {
                    update_hover(
                        &content_state,
                        HoverTarget::Content,
                        *hovered,
                        content_config,
                        window,
                        cx,
                    );
                })
                .on_mouse_down(MouseButton::Left, move |_, window, cx| {
                    hide_immediately(&pressed_state, window, cx);
                })
                .child(content)
                .into_any_element(),
        )
    } else {
        None
    };

    side_popup(
        tooltip_id,
        trigger.into_any_element(),
        content,
        SidePopupOptions {
            placement: foundation_placement(configuration.placement),
            align: foundation_alignment(configuration.alignment),
            offset: px(configuration.gap),
            margin: px(configuration.margin),
            priority: TOOLTIP_PRIORITY,
        },
        window,
        cx,
    )
}

fn update_hover(
    state: &gpui::Entity<TooltipState>,
    target: HoverTarget,
    hovered: bool,
    configuration: TooltipConfiguration,
    window: &mut Window,
    cx: &mut App,
) {
    let scheduled = state.update(cx, |state, _| {
        match target {
            HoverTarget::Trigger => state.trigger_hovered = hovered,
            HoverTarget::Content => state.content_hovered = hovered,
        }
        state.generation = state.generation.wrapping_add(1);
        let generation = state.generation;
        let any_hovered = state.trigger_hovered || state.content_hovered;
        if any_hovered && !state.visible && state.trigger_hovered {
            Some((Transition::Show, generation, configuration.show_delay_ms))
        } else if !any_hovered && state.visible {
            Some((Transition::Hide, generation, configuration.hide_delay_ms))
        } else {
            None
        }
    });

    if let Some((transition, generation, delay_ms)) = scheduled {
        schedule_transition(state.clone(), transition, generation, delay_ms, window, cx);
    }
}

fn schedule_transition(
    state: gpui::Entity<TooltipState>,
    transition: Transition,
    generation: u64,
    delay_ms: u64,
    window: &mut Window,
    cx: &mut App,
) {
    if delay_ms == 0 {
        apply_transition(&state, transition, generation, window, cx);
        return;
    }

    window
        .spawn(cx, async move |cx| {
            cx.background_executor()
                .timer(Duration::from_millis(delay_ms))
                .await;
            cx.update(|window, cx| {
                apply_transition(&state, transition, generation, window, cx);
            })
            .ok();
        })
        .detach();
}

fn apply_transition(
    state: &gpui::Entity<TooltipState>,
    transition: Transition,
    generation: u64,
    window: &mut Window,
    cx: &mut App,
) {
    let changed = state.update(cx, |state, cx| {
        if state.generation != generation {
            return false;
        }
        let should_show = state.trigger_hovered || state.content_hovered;
        let visible = match transition {
            Transition::Show => should_show,
            Transition::Hide => should_show,
        };
        if state.visible == visible {
            return false;
        }
        state.visible = visible;
        cx.notify();
        true
    });
    if changed {
        window.refresh();
    }
}

fn hide_immediately(state: &gpui::Entity<TooltipState>, window: &mut Window, cx: &mut App) {
    let changed = state.update(cx, |state, cx| {
        state.generation = state.generation.wrapping_add(1);
        state.trigger_hovered = false;
        state.content_hovered = false;
        if !state.visible {
            return false;
        }
        state.visible = false;
        cx.notify();
        true
    });
    if changed {
        window.refresh();
    }
}

pub(crate) fn foundation_placement(placement: u32) -> Placement {
    match placement {
        1 => Placement::Top,
        2 => Placement::Right,
        4 => Placement::Left,
        _ => Placement::Bottom,
    }
}

pub(crate) fn foundation_alignment(alignment: u32) -> Align {
    match alignment {
        0 => Align::Start,
        2 => Align::End,
        _ => Align::Center,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn semantic_placement_and_alignment_map_to_foundation_values() {
        assert_eq!(foundation_placement(0), Placement::Bottom);
        assert_eq!(foundation_placement(1), Placement::Top);
        assert_eq!(foundation_placement(2), Placement::Right);
        assert_eq!(foundation_placement(3), Placement::Bottom);
        assert_eq!(foundation_placement(4), Placement::Left);
        assert_eq!(foundation_alignment(0), Align::Start);
        assert_eq!(foundation_alignment(1), Align::Center);
        assert_eq!(foundation_alignment(2), Align::End);
    }
}
