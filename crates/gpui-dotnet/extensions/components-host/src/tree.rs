use std::{
    cell::{Cell, RefCell},
    collections::{HashMap, HashSet},
    rc::Rc,
};

use gpui::{
    AnyElement, App, AppContext as _, Entity, InteractiveElement as _, IntoElement as _,
    KeyDownEvent, ParentElement as _, SharedString, Styled as _, Subscription, Window, div, px,
};
use gpui_component::{
    ActiveTheme as _, Icon, IconName,
    list::ListItem,
    tree::{Tree, TreeEvent, TreeItem, TreeState},
};
use gpui_dotnet::extension::{
    NativeExtensionEventEmitter, NativeExtensionRequest, NativeExtensionStore,
};

use crate::component_schema::*;

#[derive(Clone, PartialEq, Eq)]
struct TreeNode {
    id: String,
    label: String,
    depth: u32,
    disabled: bool,
    initially_expanded: bool,
}

fn validate(config: &TreeConfiguration) -> Result<Vec<TreeNode>, SharedString> {
    let count = config.item_ids.len();
    if count > 4096
        || config.labels.len() != count
        || config.depths.len() != count
        || config.item_disabled.len() != count
        || config.initial_expanded.len() != count
    {
        return Err("Tree node batches have invalid lengths.".into());
    }
    let mut seen = HashSet::with_capacity(count);
    let mut nodes = Vec::with_capacity(count);
    for index in 0..count {
        let id = &config.item_ids[index];
        let label = &config.labels[index];
        let depth = config.depths[index];
        if id.trim().is_empty()
            || id.len() > 256
            || id.chars().any(char::is_control)
            || !seen.insert(id.clone())
            || label.trim().is_empty()
            || label.chars().any(char::is_control)
            || depth > 64
            || (index == 0 && depth != 0)
            || (index > 0 && depth > config.depths[index - 1] + 1)
            || config.item_disabled[index] > 1
            || config.initial_expanded[index] > 1
        {
            return Err(
                "Tree nodes need distinct IDs, labels, preorder depths, and valid flags.".into(),
            );
        }
        nodes.push(TreeNode {
            id: id.clone(),
            label: label.clone(),
            depth,
            disabled: config.item_disabled[index] != 0,
            initially_expanded: config.initial_expanded[index] != 0,
        });
    }
    if config.has_selection && !seen.contains(config.selected_id) {
        return Err("The selected Tree ID is absent from the batch.".into());
    }
    if !config.has_selection && !config.selected_id.is_empty() {
        return Err("Tree selection presence is inconsistent.".into());
    }
    Ok(nodes)
}

fn collect_expansion(items: &[TreeItem], expanded: &mut HashMap<String, bool>) {
    for item in items {
        expanded.insert(item.id.to_string(), item.is_expanded());
        collect_expansion(&item.children, expanded);
    }
}

fn build_items(nodes: &[TreeNode], old: &[TreeItem]) -> Vec<TreeItem> {
    let mut expanded = HashMap::new();
    collect_expansion(old, &mut expanded);
    let mut stack: Vec<(u32, TreeItem)> = Vec::with_capacity(nodes.len());
    for node in nodes.iter().rev() {
        let mut children = Vec::new();
        while stack
            .last()
            .is_some_and(|(depth, _)| *depth == node.depth + 1)
        {
            if let Some((_, child)) = stack.pop() {
                children.push(child);
            }
        }
        let item = TreeItem::new(node.id.clone(), node.label.clone())
            .children(children)
            .disabled(node.disabled)
            .expanded(*expanded.get(&node.id).unwrap_or(&node.initially_expanded));
        stack.push((node.depth, item));
    }
    stack.into_iter().rev().map(|(_, item)| item).collect()
}

struct TreeEvents {
    token: Cell<u64>,
    callback_error: Cell<Option<i32>>,
    emitter: NativeExtensionEventEmitter,
}

impl TreeEvents {
    fn emit(&self, kind: u16, id: &str) {
        let token = self.token.get();
        if token == 0 {
            return;
        }
        if let Err(status) = self.emitter.emit(token, kind, 0, 0, id.as_bytes()) {
            self.callback_error.set(Some(status));
        }
    }

    fn check_error(&self) -> Result<(), SharedString> {
        if let Some(status) = self.callback_error.take() {
            return Err(
                format!("The managed Tree event callback failed with status {status}.").into(),
            );
        }
        Ok(())
    }
}

#[derive(Clone)]
struct RetainedTree {
    state: Entity<TreeState>,
    nodes: Rc<RefCell<Vec<TreeNode>>>,
    roots: Rc<RefCell<Vec<TreeItem>>>,
    selected: Rc<RefCell<Option<String>>>,
    events: Rc<TreeEvents>,
    _subscription: Rc<Subscription>,
}

pub(super) fn tree(
    request: NativeExtensionRequest,
    resources: &NativeExtensionStore,
    _window: &mut Window,
    cx: &mut App,
) -> Result<AnyElement, SharedString> {
    if !request.children.is_empty() {
        return Err("Tree does not accept managed child elements.".into());
    }
    let config =
        TreeConfiguration::parse(&request.configuration).ok_or("Invalid Tree configuration.")?;
    let nodes = validate(&config)?;
    let selected = config.has_selection.then(|| config.selected_id.to_owned());
    let resource = resources.get_or_insert_with(&request.resource_key, || {
        let roots = build_items(&nodes, &[]);
        let state = cx.new(|cx| TreeState::new(cx).items(roots.clone()));
        if let Some(id) = selected.as_deref() {
            state.update(cx, |state, cx| {
                let cursor = state.index_of(&id.into());
                state.set_selected_index(cursor, cx);
            });
        }
        let events = Rc::new(TreeEvents {
            token: Cell::new(config.tree_event),
            callback_error: Cell::new(None),
            emitter: request.events,
        });
        let receiver = events.clone();
        let subscription = cx.subscribe(&state, move |_, event: &TreeEvent, _| match event {
            TreeEvent::Expanded(id) => receiver.emit(TREE_EVENT_EXPANDED, id.as_str()),
            TreeEvent::Collapsed(id) => receiver.emit(TREE_EVENT_COLLAPSED, id.as_str()),
        });
        RetainedTree {
            state,
            nodes: Rc::new(RefCell::new(nodes.clone())),
            roots: Rc::new(RefCell::new(roots)),
            selected: Rc::new(RefCell::new(selected.clone())),
            events,
            _subscription: Rc::new(subscription),
        }
    });
    resource.events.token.set(config.tree_event);
    resource.events.check_error()?;
    if *resource.nodes.borrow() != nodes {
        let roots = build_items(&nodes, &resource.roots.borrow());
        let cursor_id = resource
            .state
            .read(cx)
            .selected_item()
            .map(|item| item.id.clone());
        resource.state.update(cx, |state, cx| {
            state.set_items(roots.clone(), cx);
            let cursor = cursor_id.as_ref().and_then(|id| state.index_of(id));
            state.set_selected_index(cursor, cx);
        });
        *resource.roots.borrow_mut() = roots;
        *resource.nodes.borrow_mut() = nodes;
    }
    if *resource.selected.borrow() != selected {
        *resource.selected.borrow_mut() = selected;
        resource.state.update(cx, |_, cx| cx.notify());
    }

    let row_events = resource.events.clone();
    let committed = resource.selected.clone();
    let component = Tree::new(&resource.state, move |_, entry, _, _, cx| {
        let id = entry.item().id.clone();
        let selected = committed.borrow().as_deref() == Some(id.as_str());
        let icon = if entry.is_folder() {
            if entry.is_expanded() {
                IconName::FolderOpen
            } else {
                IconName::Folder
            }
        } else {
            IconName::File
        };
        let events = row_events.clone();
        ListItem::new(gpui::ElementId::Name(id.clone()))
            .w_full()
            .rounded(cx.theme().radius)
            .pl(px(entry.depth() as f32 * 16. + 8.))
            .py_0p5()
            .confirmed(selected)
            .child(
                div()
                    .flex()
                    .items_center()
                    .gap_2()
                    .child(Icon::new(icon).size(px(16.)))
                    .child(entry.item().label.clone()),
            )
            .on_click(move |_, _, _| events.emit(TREE_EVENT_SELECTION_REQUESTED, id.as_str()))
    });
    let state = resource.state.clone();
    let events = resource.events.clone();
    Ok(div()
        .size_full()
        .on_key_down(move |event: &KeyDownEvent, _, cx| {
            let modifiers = event.keystroke.modifiers;
            if event.is_held
                || event.keystroke.key.as_str() != "space"
                || modifiers.control
                || modifiers.alt
                || modifiers.platform
                || modifiers.function
                || modifiers.shift
            {
                return;
            }
            if let Some(id) = state
                .read(cx)
                .selected_item()
                .filter(|item| !item.is_disabled())
                .map(|item| item.id.clone())
            {
                cx.stop_propagation();
                events.emit(TREE_EVENT_SELECTION_REQUESTED, id.as_str());
            }
        })
        .child(component)
        .into_any_element())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn preorder_batch_builds_nested_tree_and_preserves_expansion() {
        let nodes = vec![
            TreeNode {
                id: "root".into(),
                label: "Root".into(),
                depth: 0,
                disabled: false,
                initially_expanded: true,
            },
            TreeNode {
                id: "child".into(),
                label: "Child".into(),
                depth: 1,
                disabled: false,
                initially_expanded: false,
            },
            TreeNode {
                id: "sibling".into(),
                label: "Sibling".into(),
                depth: 1,
                disabled: false,
                initially_expanded: false,
            },
            TreeNode {
                id: "other".into(),
                label: "Other".into(),
                depth: 0,
                disabled: true,
                initially_expanded: false,
            },
        ];
        let roots = build_items(&nodes, &[]);
        assert_eq!(roots.len(), 2);
        assert_eq!(roots[0].children[0].id.as_str(), "child");
        assert_eq!(roots[0].children[1].id.as_str(), "sibling");
        assert!(roots[0].is_expanded());
        let updated = vec![TreeNode {
            label: "Renamed".into(),
            initially_expanded: false,
            ..nodes[0].clone()
        }];
        let retained = build_items(&updated, &roots);
        assert!(retained[0].is_expanded());
        assert_eq!(retained[0].label.as_str(), "Renamed");
    }

    #[test]
    fn generated_tree_batch_rejects_invalid_shape_and_selection() {
        let valid = TreeConfiguration::parse(
            "[\"src\",\"src/main\"]\n[\"src\",\"main\"]\n0,1\n0,0\n1,0\nsrc/main\n1\n7",
        )
        .unwrap();
        assert_eq!(validate(&valid).unwrap().len(), 2);
        let invalid_depth = TreeConfiguration::parse(
            "[\"src\",\"src/main\"]\n[\"src\",\"main\"]\n0,2\n0,0\n1,0\nsrc/main\n1\n7",
        )
        .unwrap();
        assert!(validate(&invalid_depth).is_err());
        let invalid_selection =
            TreeConfiguration::parse("[\"src\"]\n[\"src\"]\n0\n0\n1\nmissing\n1\n7").unwrap();
        assert!(validate(&invalid_selection).is_err());
        let invalid_label =
            TreeConfiguration::parse("[\"src\"]\n[\"bad\\nlabel\"]\n0\n0\n1\n\n0\n7").unwrap();
        assert!(validate(&invalid_label).is_err());
    }
}
