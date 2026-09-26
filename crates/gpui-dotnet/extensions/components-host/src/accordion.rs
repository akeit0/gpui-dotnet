use std::{
    cell::{Cell, RefCell},
    collections::HashSet,
    rc::Rc,
};

use gpui::{
    AnyElement, App, FocusHandle, InteractiveElement as _, IntoElement as _, KeyDownEvent,
    ParentElement as _, SharedString, Styled as _, Window, div, percentage,
    prelude::FluentBuilder as _, rems,
};
use gpui_base::{
    Accordion as BaseAccordion, AccordionHeader, AccordionItem, AccordionPanel, AccordionTrigger,
    MotionReveal, spring,
};
use gpui_component::{ActiveTheme as _, Icon, IconName, Sizable as _, StyledExt as _};
use gpui_dotnet::extension::{
    NativeExtensionEventEmitter, NativeExtensionRequest, NativeExtensionStore,
};

use crate::component_schema::*;

struct AccordionInteraction {
    focus: FocusHandle,
    cursor: Cell<Option<u32>>,
}

fn validate(config: &AccordionConfiguration, child_count: usize) -> Result<(), SharedString> {
    let count = config.item_ids.len();
    if count > 256
        || config.titles.len() != count
        || config.item_disabled.len() != count
        || child_count != count
        || config.open_ids.len() > count
        || (!config.multiple && config.open_ids.len() > 1)
    {
        return Err("Accordion batch lengths or open count are invalid.".into());
    }
    let mut known = HashSet::with_capacity(count);
    for ((id, title), disabled) in config
        .item_ids
        .iter()
        .zip(&config.titles)
        .zip(&config.item_disabled)
    {
        if *id == 0
            || !known.insert(*id)
            || title.trim().is_empty()
            || title.len() > 1024
            || title.chars().any(char::is_control)
            || *disabled > 1
        {
            return Err(
                "Accordion items need distinct nonzero IDs, titles, and valid disabled flags."
                    .into(),
            );
        }
    }
    let mut open = HashSet::with_capacity(config.open_ids.len());
    if config
        .open_ids
        .iter()
        .any(|id| !known.contains(id) || !open.insert(*id))
    {
        return Err("Open Accordion IDs must be distinct items.".into());
    }
    Ok(())
}

fn next_open(open: &mut Vec<u32>, id: u32, multiple: bool, order: &[u32]) -> Vec<u8> {
    if let Some(index) = open.iter().position(|value| *value == id) {
        open.remove(index);
    } else {
        if !multiple {
            open.clear();
        }
        open.push(id);
    }
    let mut payload = Vec::with_capacity(open.len() * 4);
    for id in order {
        if open.contains(id) {
            payload.extend_from_slice(&id.to_le_bytes());
        }
    }
    payload
}

fn request_toggle(
    open: &RefCell<Vec<u32>>,
    id: u32,
    multiple: bool,
    order: &[u32],
    events: NativeExtensionEventEmitter,
    token: u64,
) {
    let payload = next_open(&mut open.borrow_mut(), id, multiple, order);
    let _ = events.emit(token, ACCORDION_EVENT_CHANGED, 0, 0, &payload);
}

fn key_target(
    key: &str,
    cursor: Option<u32>,
    ids: &[u32],
    disabled: &[u32],
) -> Option<(u32, bool)> {
    let enabled: Vec<u32> = ids
        .iter()
        .zip(disabled)
        .filter_map(|(id, flag)| (*flag == 0).then_some(*id))
        .collect();
    if enabled.is_empty() {
        return None;
    }
    let position = cursor.and_then(|id| enabled.iter().position(|candidate| *candidate == id));
    match key {
        "home" => Some((enabled[0], false)),
        "end" => Some((*enabled.last()?, false)),
        "up" => Some((enabled[position.unwrap_or(0).saturating_sub(1)], false)),
        "down" => Some((
            enabled[(position.map_or(0, |index| index + 1)).min(enabled.len() - 1)],
            false,
        )),
        "enter" | "space" => position.map(|index| (enabled[index], true)),
        _ => None,
    }
}

pub(super) fn accordion(
    request: NativeExtensionRequest,
    resources: &NativeExtensionStore,
    window: &mut Window,
    cx: &mut App,
) -> Result<AnyElement, SharedString> {
    let config = AccordionConfiguration::parse(&request.configuration)
        .ok_or("Invalid Accordion configuration.")?;
    validate(&config, request.children.len())?;
    let interaction = resources.get_or_insert_with(&request.resource_key, || {
        Rc::new(AccordionInteraction {
            focus: cx.focus_handle(),
            cursor: Cell::new(None),
        })
    });
    if interaction.cursor.get().is_some_and(|id| {
        !config
            .item_ids
            .iter()
            .zip(&config.item_disabled)
            .any(|(candidate, disabled)| *candidate == id && *disabled == 0)
    }) {
        interaction.cursor.set(None);
    }
    if interaction.cursor.get().is_none() {
        interaction.cursor.set(
            config
                .item_ids
                .iter()
                .zip(&config.item_disabled)
                .find(|(_, disabled)| **disabled == 0)
                .map(|(id, _)| *id),
        );
    }
    let group_id = format!(
        "gpui-net-accordion:{}:{}",
        request.resource_key.owner_view(),
        request.resource_key.key()
    );
    let focus = interaction.focus.clone().tab_stop(
        !config.disabled && config.changed_event != 0 && config.item_disabled.contains(&0),
    );
    let open = Rc::new(RefCell::new(config.open_ids.clone()));
    let mut group = BaseAccordion::new(group_id.clone()).v_flex().w_full();
    if config.bordered {
        group = group
            .border_1()
            .border_color(cx.theme().border)
            .rounded(cx.theme().radius_lg)
            .overflow_hidden();
    }
    let size = match config.size {
        AccordionSize::Xsmall => 0,
        AccordionSize::Small => 1,
        AccordionSize::Medium => 2,
        AccordionSize::Large => 3,
    };
    let last = config.item_ids.len().saturating_sub(1);
    for (index, (((id, title), disabled), content)) in config
        .item_ids
        .iter()
        .zip(&config.titles)
        .zip(&config.item_disabled)
        .zip(request.children)
        .enumerate()
    {
        let id = *id;
        let item_disabled = config.disabled || *disabled != 0;
        let is_open = config.open_ids.contains(&id);
        let item_key = format!("{group_id}:{id}");
        let progress = spring(
            (item_key.clone(), "panel"),
            if is_open { 1. } else { 0. },
            cx.theme().motion_tokens().spring_control,
            window,
            cx,
        );
        let mut trigger = AccordionTrigger::new(format!("{item_key}:trigger"))
            .open(is_open)
            .disabled(item_disabled)
            .h_flex()
            .justify_between()
            .gap_3()
            .font_medium()
            .text_size(match size {
                0 => rems(0.8125),
                3 => rems(1.0),
                _ => rems(0.875),
            })
            .border_l_2()
            .border_color(
                if !item_disabled
                    && focus.is_focused(window)
                    && interaction.cursor.get() == Some(id)
                {
                    cx.theme().ring
                } else {
                    cx.theme().transparent
                },
            )
            .child(div().flex_1().min_w_0().child(title.clone()));
        trigger = match size {
            0 => trigger.py_1().px_1p5(),
            1 => trigger.py_1p5().px_2(),
            3 => trigger.py_3().px_4(),
            _ => trigger.py_2().px_3(),
        };
        if !item_disabled {
            trigger = trigger.child(
                Icon::new(IconName::ChevronDown)
                    .xsmall()
                    .flex_none()
                    .text_color(cx.theme().muted_foreground)
                    .rotate(percentage(if is_open { 0.5 } else { 0. })),
            );
        }
        if !item_disabled && config.changed_event != 0 {
            let open = open.clone();
            let order = config.item_ids.clone();
            let multiple = config.multiple;
            let token = config.changed_event;
            let events = request.events;
            let pointer_focus = focus.clone();
            let interaction = interaction.clone();
            trigger = trigger.on_change(move |_, _, window, cx| {
                interaction.cursor.set(Some(id));
                pointer_focus.focus(window, cx);
                request_toggle(&open, id, multiple, &order, events, token);
            });
        }
        let mut content_box = div().children([content]);
        content_box = match size {
            0 => content_box.pb_1().px_1p5(),
            1 => content_box.pb_1p5().px_2(),
            3 => content_box.pb_3().px_4(),
            _ => content_box.pb_2().px_3(),
        };
        let item = AccordionItem::new()
            .open(is_open)
            .disabled(item_disabled)
            .header(
                AccordionHeader::new(trigger)
                    .id(format!("{item_key}:header"))
                    .w_full(),
            )
            .panel(
                AccordionPanel::new()
                    .id(format!("{item_key}:panel"))
                    .open(is_open)
                    .keep_mounted(true)
                    .w_full()
                    .child(MotionReveal::new(
                        format!("{item_key}:content"),
                        progress,
                        content_box.into_any_element(),
                    )),
            )
            .v_flex()
            .w_full()
            .bg(cx.theme().tokens.accordion)
            .overflow_hidden()
            .when(index != last, |this| {
                this.border_b_1().border_color(cx.theme().border)
            });
        group = group.child(item);
    }
    let keyboard_focus = focus.clone();
    let keyboard_interaction = interaction.clone();
    let ids = config.item_ids;
    let disabled = config.item_disabled;
    let multiple = config.multiple;
    let token = config.changed_event;
    let events = request.events;
    let keyboard_enabled = !config.disabled && token != 0;
    Ok(div()
        .id(format!("{group_id}:focus"))
        .track_focus(&focus)
        .on_key_down(move |event: &KeyDownEvent, window, cx| {
            if !keyboard_enabled || !keyboard_focus.is_focused(window) {
                return;
            }
            let modifiers = event.keystroke.modifiers;
            if modifiers.control
                || modifiers.alt
                || modifiers.platform
                || modifiers.function
                || modifiers.shift
            {
                return;
            }
            if let Some((id, activate)) = key_target(
                event.keystroke.key.as_str(),
                keyboard_interaction.cursor.get(),
                &ids,
                &disabled,
            ) {
                cx.stop_propagation();
                keyboard_interaction.cursor.set(Some(id));
                window.refresh();
                if activate && !event.is_held {
                    request_toggle(&open, id, multiple, &ids, events, token);
                }
            }
        })
        .child(group)
        .into_any_element())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn open_sets_follow_stable_ids_and_single_multiple_policy() {
        let mut open = vec![10];
        assert_eq!(
            next_open(&mut open, 20, false, &[20, 10, 30]),
            20u32.to_le_bytes()
        );
        assert!(next_open(&mut open, 20, false, &[20, 10, 30]).is_empty());
        open = vec![10];
        let mut expected = Vec::new();
        expected.extend_from_slice(&20u32.to_le_bytes());
        expected.extend_from_slice(&10u32.to_le_bytes());
        assert_eq!(next_open(&mut open, 20, true, &[20, 10, 30]), expected);
    }

    #[test]
    fn keyboard_skips_disabled_items() {
        assert_eq!(
            key_target("down", Some(10), &[10, 20, 30], &[0, 1, 0]),
            Some((30, false))
        );
        assert_eq!(
            key_target("space", Some(20), &[10, 20, 30], &[0, 1, 0]),
            None
        );
    }

    #[test]
    fn batches_reject_missing_children_and_ambiguous_open_sets() {
        let config = AccordionConfiguration::parse(
            "medium\n10,20\n[\"First\",\"Second\"]\n0,1\n10\n0\n1\n0\n23",
        )
        .unwrap();
        assert!(validate(&config, 2).is_ok());
        assert!(validate(&config, 1).is_err());
        let duplicate = AccordionConfiguration::parse(
            "medium\n10,20\n[\"First\",\"Second\"]\n0,1\n10,10\n1\n1\n0\n23",
        )
        .unwrap();
        assert!(validate(&duplicate, 2).is_err());
    }
}
