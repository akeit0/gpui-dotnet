use std::collections::{HashMap, HashSet};

use crate::{extension::NativeExtensionResourceKey, resources::ResourceKey};

/// Small command-ingress index. Only snapshot acceptance mutates resource presence;
/// worker ingress reads generations without touching GPUI entities or retained trees.
#[derive(Default)]
pub(crate) struct ResourcePresence {
    base: HashMap<(u16, ResourceKey), u64>,
    extensions: HashMap<NativeExtensionResourceKey, u64>,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn presence_is_scoped_by_owner_and_resource_kind() {
        let key = ResourceKey::new(1, "control".into());
        let foreign = ResourceKey::new(2, "control".into());
        let mut presence = ResourcePresence::default();
        presence.accept(&HashSet::from([(1, key.clone())]), &HashSet::new(), 1);
        assert_eq!(presence.base_generation(1, &key), Some(1));
        assert_eq!(presence.base_generation(2, &key), None);
        assert_eq!(presence.base_generation(1, &foreign), None);
    }
}

impl ResourcePresence {
    pub(crate) fn accept(
        &mut self,
        base: &HashSet<(u16, ResourceKey)>,
        extensions: &HashSet<NativeExtensionResourceKey>,
        revision: u64,
    ) {
        self.base.retain(|key, _| base.contains(key));
        self.extensions.retain(|key, _| extensions.contains(key));
        for key in base {
            self.base.entry(key.clone()).or_insert(revision);
        }
        for key in extensions {
            self.extensions.entry(key.clone()).or_insert(revision);
        }
    }

    pub(crate) fn base_generation(&self, kind: u16, key: &ResourceKey) -> Option<u64> {
        self.base.get(&(kind, key.clone())).copied()
    }

    pub(crate) fn extension_generation(&self, key: &NativeExtensionResourceKey) -> Option<u64> {
        self.extensions.get(key).copied()
    }
}
