use gpui::{SharedString, StatefulInteractiveElement};

use crate::{
    semantic::{OP_ACCESSIBLE_DESCRIPTION, OP_ACCESSIBLE_NAME},
    snapshot::{SnapshotNode, ValidatedSnapshot},
};

/// Snapshot-owned strings are interned before managed render arenas can be reused.
#[derive(Clone, Debug, Default, PartialEq)]
pub(crate) struct Accessibility {
    pub(crate) name: Option<SharedString>,
    pub(crate) description: Option<SharedString>,
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{native_workloads::WorkloadArena, semantic::*};
    use gpui::{Element, InteractiveElement, Role, accesskit, div};

    #[test]
    fn metadata_is_interned_last_wins_and_maps_to_native_accessibility() {
        for (component, data) in [
            (COMPONENT_BUTTON, "button"),
            (COMPONENT_CHECKBOX, "checkbox"),
            (COMPONENT_RADIO, "radio"),
            (COMPONENT_INPUT, "input\0\0"),
            (COMPONENT_SLIDER, "slider"),
        ] {
            let mut arena = WorkloadArena::default();
            let root = arena.node_with_data(component, None, data);
            if matches!(component, COMPONENT_INPUT | COMPONENT_SLIDER) {
                arena.op(root, OP_RESOURCE_OWNER, 1);
            }
            arena.data_op(root, OP_ACCESSIBLE_NAME, "Old name");
            arena.data_op(root, OP_ACCESSIBLE_NAME, "音量");
            arena.data_op(root, OP_ACCESSIBLE_DESCRIPTION, "Playback volume");
            let snapshot = arena.decode();
            drop(arena);
            let node = &snapshot.nodes[0];
            let metadata = Accessibility::from_snapshot(node, &snapshot);
            if component == COMPONENT_INPUT {
                assert_eq!(
                    crate::resources::input_configuration(&snapshot, node)
                        .unwrap()
                        .presentation
                        .accessibility,
                    metadata
                );
            }
            if component == COMPONENT_SLIDER {
                assert_eq!(
                    crate::resources::slider_configuration(&snapshot, node)
                        .unwrap()
                        .presentation
                        .accessibility,
                    metadata
                );
            }
            drop(snapshot);
            let mut info = accesskit::Node::new(Role::Button);
            metadata
                .apply(div().id("control"))
                .write_a11y_info(&mut info);
            assert_eq!(info.label(), Some("音量"));
            assert_eq!(info.description(), Some("Playback volume"));

            let mut arena = WorkloadArena::default();
            arena.node_with_data(COMPONENT_BUTTON, None, "button");
            let snapshot = arena.decode();
            assert_eq!(
                Accessibility::from_snapshot(&snapshot.nodes[0], &snapshot),
                Accessibility::default()
            );
        }
    }

    #[test]
    fn metadata_operations_reject_unsupported_components() {
        for code in [OP_ACCESSIBLE_NAME, OP_ACCESSIBLE_DESCRIPTION] {
            let mut arena = WorkloadArena::default();
            let root = arena.node(COMPONENT_DIV, None);
            arena.data_op(root, code, "Name");
            assert!(
                arena
                    .decode_into(&mut ValidatedSnapshot::default())
                    .is_err()
            );
        }
    }
}

impl Accessibility {
    pub(crate) fn from_snapshot(node: &SnapshotNode, snapshot: &ValidatedSnapshot) -> Self {
        Self {
            name: snapshot.last_data_op(node, OP_ACCESSIBLE_NAME),
            description: snapshot.last_data_op(node, OP_ACCESSIBLE_DESCRIPTION),
        }
    }

    pub(crate) fn apply<T: StatefulInteractiveElement>(&self, mut element: T) -> T {
        if let Some(name) = &self.name {
            element = element.aria_label(name.clone());
        }
        if let Some(description) = &self.description {
            element = element.aria_description(description.clone());
        }
        element
    }
}
