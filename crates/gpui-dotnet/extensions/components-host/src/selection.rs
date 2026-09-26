use std::{
    cell::{Cell, RefCell},
    collections::{HashMap, HashSet},
    rc::Rc,
};

use gpui::{
    AnyElement, App, AppContext as _, Entity, IntoElement as _, SharedString, Subscription, Window,
};
use gpui_component::{
    IndexPath, Sizable as _,
    combobox::{Combobox, ComboboxEvent, ComboboxState},
    searchable_list::{SearchableListItem, SearchableVec},
    select::{Select, SelectEvent, SelectState},
};
use gpui_dotnet::extension::{
    NativeExtensionEventEmitter, NativeExtensionRequest, NativeExtensionStore,
};

use crate::component_schema::*;

#[derive(Clone, PartialEq, Eq)]
struct Choice {
    id: u32,
    label: SharedString,
    disabled: bool,
}

impl SearchableListItem for Choice {
    type Value = u32;

    fn title(&self) -> SharedString {
        self.label.clone()
    }
    fn value(&self) -> &u32 {
        &self.id
    }
    fn disabled(&self) -> bool {
        self.disabled
    }
}

type Choices = SearchableVec<Choice>;

#[derive(Clone, PartialEq, Eq)]
struct SelectionSnapshot {
    items: Vec<Choice>,
    selected: Vec<u32>,
}

fn snapshot(
    labels: Vec<String>,
    ids: Vec<u32>,
    disabled: Vec<u32>,
    selected: Vec<u32>,
    multiple: bool,
) -> Result<SelectionSnapshot, SharedString> {
    let count = labels.len();
    if count > 4096
        || ids.len() != count
        || disabled.len() != count
        || (!multiple && selected.len() > 1)
    {
        return Err("Selection item batches have invalid lengths.".into());
    }
    let mut known = HashSet::with_capacity(count);
    let mut items = Vec::with_capacity(count);
    for ((label, id), flag) in labels.into_iter().zip(ids).zip(disabled) {
        if id == 0 || label.trim().is_empty() || flag > 1 || !known.insert(id) {
            return Err(
                "Selection items need distinct nonzero IDs, labels, and valid disabled flags."
                    .into(),
            );
        }
        items.push(Choice {
            id,
            label: label.into(),
            disabled: flag != 0,
        });
    }
    let mut selected_seen = HashSet::with_capacity(selected.len());
    if selected
        .iter()
        .any(|id| !known.contains(id) || !selected_seen.insert(*id))
    {
        return Err("Selected IDs must be distinct items in the batch.".into());
    }
    Ok(SelectionSnapshot { items, selected })
}

fn indices(snapshot: &SelectionSnapshot) -> Vec<IndexPath> {
    let positions: HashMap<u32, usize> = snapshot
        .items
        .iter()
        .enumerate()
        .map(|(index, item)| (item.id, index))
        .collect();
    snapshot
        .selected
        .iter()
        .filter_map(|id| positions.get(id).copied().map(IndexPath::new))
        .collect()
}

struct SelectionEvents {
    token: Cell<u64>,
    dirty: Cell<bool>,
    callback_error: Cell<Option<i32>>,
    emitter: NativeExtensionEventEmitter,
}

impl SelectionEvents {
    fn emit_select(&self, id: Option<u32>) {
        self.dirty.set(true);
        let token = self.token.get();
        if token == 0 {
            return;
        }
        let payload = id.map(u32::to_le_bytes);
        if let Err(status) = self.emitter.emit(
            token,
            SELECT_EVENT_SELECTED,
            0,
            0,
            payload.as_ref().map_or(&[][..], |bytes| bytes.as_slice()),
        ) {
            self.callback_error.set(Some(status));
        }
    }

    fn emit_combo(&self, ids: &[u32]) {
        self.dirty.set(true);
        let token = self.token.get();
        if token == 0 {
            return;
        }
        let mut payload = Vec::with_capacity(ids.len() * 4);
        for id in ids {
            payload.extend_from_slice(&id.to_le_bytes());
        }
        if let Err(status) = self
            .emitter
            .emit(token, COMBOBOX_EVENT_CHANGED, 0, 0, &payload)
        {
            self.callback_error.set(Some(status));
        }
    }

    fn check_error(&self) -> Result<(), SharedString> {
        if let Some(status) = self.callback_error.take() {
            return Err(format!(
                "The managed selection event callback failed with status {status}."
            )
            .into());
        }
        Ok(())
    }
}

#[derive(Clone)]
struct RetainedSelect {
    state: Entity<SelectState<Choices>>,
    accepted: Rc<RefCell<SelectionSnapshot>>,
    events: Rc<SelectionEvents>,
    searchable: bool,
    _subscription: Rc<Subscription>,
}

#[derive(Clone)]
struct RetainedCombobox {
    state: Entity<ComboboxState<Choices>>,
    accepted: Rc<RefCell<SelectionSnapshot>>,
    events: Rc<SelectionEvents>,
    multiple: bool,
    _subscription: Rc<Subscription>,
}

pub(super) fn select(
    request: NativeExtensionRequest,
    resources: &NativeExtensionStore,
    window: &mut Window,
    cx: &mut App,
) -> Result<AnyElement, SharedString> {
    if !request.children.is_empty() {
        return Err("Select does not accept children.".into());
    }
    let config = SelectConfiguration::parse(&request.configuration)
        .ok_or("Invalid Select configuration.")?;
    let next = snapshot(
        config.labels,
        config.item_ids,
        config.item_disabled,
        config.selected_ids,
        false,
    )?;
    let selected = indices(&next).into_iter().next();
    let resource = resources.get_or_insert_with(&request.resource_key, || {
        let state = cx.new(|cx| {
            SelectState::new(Choices::new(next.items.clone()), selected, window, cx)
                .searchable(config.searchable)
        });
        let events = Rc::new(SelectionEvents {
            token: Cell::new(config.selected_event),
            dirty: Cell::new(false),
            callback_error: Cell::new(None),
            emitter: request.events,
        });
        let receiver = events.clone();
        let subscription = window.subscribe(
            &state,
            cx,
            move |_, emitted: &SelectEvent<Choices>, _, _| {
                let SelectEvent::Confirm(id) = emitted;
                receiver.emit_select(*id);
            },
        );
        RetainedSelect {
            state,
            accepted: Rc::new(RefCell::new(next.clone())),
            events,
            searchable: config.searchable,
            _subscription: Rc::new(subscription),
        }
    });
    if resource.searchable != config.searchable {
        return Err("Select search mode is fixed for a retained key.".into());
    }
    resource.events.token.set(config.selected_event);
    resource.events.check_error()?;
    let previous = resource.accepted.borrow().clone();
    if previous.items != next.items {
        resource.state.update(cx, |state, cx| {
            state.set_items(Choices::new(next.items.clone()), window, cx);
            state.set_selected_value(&0, window, cx);
        });
    }
    let dirty = resource.events.dirty.replace(false);
    if previous.items != next.items || previous.selected != next.selected || dirty {
        let native_selected = resource.state.read(cx).selected_value().copied();
        if previous.items != next.items || native_selected != next.selected.first().copied() {
            resource.state.update(cx, |state, cx| {
                state.set_selected_index(selected, window, cx)
            });
        }
    }
    *resource.accepted.borrow_mut() = next;
    let mut element = Select::new(&resource.state)
        .with_size(select_size(config.size))
        .placeholder(config.placeholder)
        .search_placeholder(config.search_placeholder)
        .cleanable(config.cleanable)
        .disabled(config.disabled);
    if !config.accessibility_label.is_empty() {
        element = element.accessibility_label(config.accessibility_label);
    }
    Ok(element.into_any_element())
}

pub(super) fn combobox(
    request: NativeExtensionRequest,
    resources: &NativeExtensionStore,
    window: &mut Window,
    cx: &mut App,
) -> Result<AnyElement, SharedString> {
    if !request.children.is_empty() {
        return Err("Combobox does not accept children.".into());
    }
    let config = ComboboxConfiguration::parse(&request.configuration)
        .ok_or("Invalid Combobox configuration.")?;
    let next = snapshot(
        config.labels,
        config.item_ids,
        config.item_disabled,
        config.selected_ids,
        config.multiple,
    )?;
    let selected = indices(&next);
    let resource = resources.get_or_insert_with(&request.resource_key, || {
        let state = cx.new(|cx| {
            ComboboxState::new(Choices::new(next.items.clone()), selected, window, cx)
                .multiple(config.multiple)
                .searchable(true)
        });
        let events = Rc::new(SelectionEvents {
            token: Cell::new(config.changed_event),
            dirty: Cell::new(false),
            callback_error: Cell::new(None),
            emitter: request.events,
        });
        let receiver = events.clone();
        let subscription = window.subscribe(
            &state,
            cx,
            move |_, emitted: &ComboboxEvent<Choices>, _, _| {
                if let ComboboxEvent::Change(ids) = emitted {
                    receiver.emit_combo(ids);
                }
            },
        );
        RetainedCombobox {
            state,
            accepted: Rc::new(RefCell::new(next.clone())),
            events,
            multiple: config.multiple,
            _subscription: Rc::new(subscription),
        }
    });
    if resource.multiple != config.multiple {
        return Err("Combobox selection mode is fixed for a retained key.".into());
    }
    resource.events.token.set(config.changed_event);
    resource.events.check_error()?;
    let previous = resource.accepted.borrow().clone();
    if previous.items != next.items {
        resource.state.update(cx, |state, cx| {
            state.set_items(Choices::new(next.items.clone()), window, cx)
        });
    }
    let dirty = resource.events.dirty.replace(false);
    if previous.items != next.items || previous.selected != next.selected || dirty {
        let native_selected = resource.state.read(cx).selected_values();
        if previous.items != next.items || native_selected != next.selected {
            resource.state.update(cx, |state, cx| {
                state.set_selected_values(&next.selected, window, cx)
            });
        }
    }
    *resource.accepted.borrow_mut() = next;
    let element = Combobox::new(&resource.state)
        .with_size(combobox_size(config.size))
        .placeholder(config.placeholder)
        .search_placeholder(config.search_placeholder)
        .cleanable(config.cleanable)
        .disabled(config.disabled);
    Ok(element.into_any_element())
}

fn select_size(size: SelectSize) -> gpui_component::Size {
    match size {
        SelectSize::Xsmall => gpui_component::Size::XSmall,
        SelectSize::Small => gpui_component::Size::Small,
        SelectSize::Medium => gpui_component::Size::Medium,
        SelectSize::Large => gpui_component::Size::Large,
    }
}

fn combobox_size(size: ComboboxSize) -> gpui_component::Size {
    match size {
        ComboboxSize::Xsmall => gpui_component::Size::XSmall,
        ComboboxSize::Small => gpui_component::Size::Small,
        ComboboxSize::Medium => gpui_component::Size::Medium,
        ComboboxSize::Large => gpui_component::Size::Large,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn selection_snapshot_validates_ids_and_preserves_selected_identity() {
        let valid = snapshot(
            vec!["Ready".into(), "Blocked".into()],
            vec![10, 20],
            vec![0, 1],
            vec![20],
            false,
        )
        .unwrap();
        assert_eq!(indices(&valid), vec![IndexPath::new(1)]);
        assert!(
            snapshot(
                vec!["A".into(), "B".into()],
                vec![1, 1],
                vec![0, 0],
                vec![],
                true
            )
            .is_err()
        );
        assert!(snapshot(vec!["A".into()], vec![0], vec![0], vec![], false).is_err());
        assert!(snapshot(vec!["A".into()], vec![1], vec![2], vec![], false).is_err());
        assert!(snapshot(vec!["A".into()], vec![1], vec![0], vec![2], false).is_err());
        assert!(snapshot(vec!["A".into()], vec![1], vec![0], vec![1, 1], true).is_err());
        assert!(
            snapshot(
                vec!["A".into(), "B".into()],
                vec![1, 2],
                vec![0, 0],
                vec![1, 2],
                false
            )
            .is_err()
        );
    }
}
