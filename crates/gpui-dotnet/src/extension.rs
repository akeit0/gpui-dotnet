use std::{
    any::Any,
    cell::RefCell,
    collections::{HashMap, HashSet},
    sync::{Arc, OnceLock},
};

use gpui::{AnyElement, App, SharedString, Window};

use crate::{
    abi::{ManagedCallbacks, NativeControlEvent},
    snapshot::SnapshotNode,
};

/// One extension schema compiled into a native host.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct NativeExtensionDescriptor {
    pub id: &'static str,
    pub version: u32,
    pub schema_hash: u64,
}

/// Stable identity of one retained resource owned by an extension node.
#[derive(Clone, Debug, Eq, Hash, PartialEq)]
pub struct NativeExtensionResourceKey {
    owner_view: u32,
    extension_id: SharedString,
    component_kind: SharedString,
    key: SharedString,
    version: u32,
    schema_hash: u64,
}

impl NativeExtensionResourceKey {
    pub fn owner_view(&self) -> u32 {
        self.owner_view
    }

    pub fn extension_id(&self) -> &str {
        self.extension_id.as_ref()
    }

    pub fn component_kind(&self) -> &str {
        self.component_kind.as_ref()
    }

    pub fn key(&self) -> &str {
        self.key.as_ref()
    }
}

/// Decoded opaque declaration delivered to a build-time native extension provider.
pub struct NativeExtensionRequest {
    pub resource_key: NativeExtensionResourceKey,
    pub configuration: SharedString,
    pub children: Vec<AnyElement>,
    pub commands: Vec<NativeExtensionCommand>,
    pub events: NativeExtensionEventEmitter,
}

/// Extension-neutral route back to a render-bound managed callback.
#[derive(Clone, Copy)]
pub struct NativeExtensionEventEmitter {
    session_id: u64,
    owner_view: u32,
    callbacks: ManagedCallbacks,
}

impl NativeExtensionEventEmitter {
    pub(crate) fn new(session_id: u64, owner_view: u32, callbacks: ManagedCallbacks) -> Self {
        Self {
            session_id,
            owner_view,
            callbacks,
        }
    }

    /// Copies one extension-defined event into the managed callback before returning.
    pub fn emit(
        &self,
        token: u64,
        kind: u16,
        flags: u16,
        revision: u64,
        payload: &[u8],
    ) -> Result<(), i32> {
        if token == 0 {
            return Ok(());
        }
        if (token >> 32) != u64::from(self.owner_view)
            || token as u32 == 0
            || kind == 0
            || kind >= 0x8000
            || payload.len() > i32::MAX as usize
        {
            return Err(-86);
        }
        let event = NativeControlEvent {
            kind: kind | 0x8000,
            flags,
            reserved: 0,
            revision,
            data: payload.as_ptr(),
            data_length: payload.len() as i32,
            reserved2: 0,
        };
        let callback = self.callbacks.control_event.ok_or(-86)?;
        let status = unsafe { callback(self.session_id, token, &event) };
        if status == 0 { Ok(()) } else { Err(status) }
    }
}

/// One owned, extension-defined command delivered on the GPUI application thread.
#[derive(Clone, Debug)]
pub struct NativeExtensionCommand {
    pub resource_key: NativeExtensionResourceKey,
    pub command: u16,
    pub flags: u16,
    pub expected_revision: u64,
    pub payload: Arc<[u8]>,
}

/// Per-managed-View type-erased storage for extension-owned retained native state.
///
/// Values are removed when their declaring extension node disappears from the committed snapshot.
pub struct NativeExtensionStore {
    resources: RefCell<HashMap<NativeExtensionResourceKey, Box<dyn Any>>>,
    pending_commands: RefCell<HashMap<NativeExtensionResourceKey, Vec<NativeExtensionCommand>>>,
}

impl NativeExtensionStore {
    pub(crate) fn new() -> Self {
        Self {
            resources: RefCell::new(HashMap::new()),
            pending_commands: RefCell::new(HashMap::new()),
        }
    }

    pub fn get<T>(&self, key: &NativeExtensionResourceKey) -> Option<T>
    where
        T: Any + Clone,
    {
        self.resources
            .borrow()
            .get(key)
            .and_then(|value| value.downcast_ref::<T>())
            .cloned()
    }

    pub fn get_or_insert_with<T>(
        &self,
        key: &NativeExtensionResourceKey,
        create: impl FnOnce() -> T,
    ) -> T
    where
        T: Any + Clone,
    {
        if let Some(existing) = self.get(key) {
            return existing;
        }

        let created = create();
        self.resources
            .borrow_mut()
            .insert(key.clone(), Box::new(created.clone()));
        created
    }

    pub(crate) fn retain(&self, active: &HashSet<NativeExtensionResourceKey>) {
        self.resources
            .borrow_mut()
            .retain(|key, _| active.contains(key));
        self.pending_commands
            .borrow_mut()
            .retain(|key, _| active.contains(key));
    }

    pub(crate) fn enqueue_command(&self, command: NativeExtensionCommand) {
        self.pending_commands
            .borrow_mut()
            .entry(command.resource_key.clone())
            .or_default()
            .push(command);
    }

    pub(crate) fn take_commands(
        &self,
        key: &NativeExtensionResourceKey,
    ) -> Vec<NativeExtensionCommand> {
        self.pending_commands
            .borrow_mut()
            .remove(key)
            .unwrap_or_default()
    }
}

/// Native adapter compiled into a custom GPUI.NET host.
///
/// Rust/GPUI values never cross a dynamic-library boundary: a custom host links the selected
/// providers and the `gpui-dotnet` runtime into one binary.
pub trait NativeExtension: Sync {
    fn descriptor(&self) -> NativeExtensionDescriptor;

    /// Performs schema-specific validation before a command is copied into a View's UI queue.
    fn validate_command(&self, _command: &NativeExtensionCommand) -> bool {
        false
    }

    fn materialize(
        &self,
        request: NativeExtensionRequest,
        resources: &NativeExtensionStore,
        window: &mut Window,
        cx: &mut App,
    ) -> Result<AnyElement, SharedString>;
}

static EXTENSIONS: OnceLock<&'static [&'static dyn NativeExtension]> = OnceLock::new();

/// Installs the complete provider set for a custom host. Call once before returning its API table.
pub fn install_native_extensions(
    extensions: &'static [&'static dyn NativeExtension],
) -> Result<(), &'static str> {
    for (index, extension) in extensions.iter().enumerate() {
        let descriptor = extension.descriptor();
        if !valid_identifier(descriptor.id)
            || descriptor.version == 0
            || descriptor.schema_hash == 0
        {
            return Err("a native extension has an invalid descriptor");
        }
        if extensions[..index]
            .iter()
            .any(|candidate| candidate.descriptor().id == descriptor.id)
        {
            return Err("native extension identifiers must be unique");
        }
    }
    EXTENSIONS
        .set(extensions)
        .map_err(|_| "native extensions were already installed")
}

pub(crate) fn supports(id: &str, version: u32, schema_hash: u64) -> Result<(), i32> {
    if !valid_identifier(id) || version == 0 || schema_hash == 0 {
        return Err(-80);
    }
    let Some(provider) = provider(id) else {
        return Err(-81);
    };
    let descriptor = provider.descriptor();
    if descriptor.version != version || descriptor.schema_hash != schema_hash {
        return Err(-82);
    }
    Ok(())
}

pub(crate) fn provider(id: &str) -> Option<&'static dyn NativeExtension> {
    EXTENSIONS
        .get()
        .into_iter()
        .flat_map(|extensions| extensions.iter().copied())
        .find(|candidate| candidate.descriptor().id == id)
}

pub(crate) struct NativeExtensionDeclaration {
    pub extension_id: SharedString,
    pub component_kind: SharedString,
    pub key: SharedString,
    pub version: u32,
    pub schema_hash: u64,
    pub configuration: SharedString,
}

impl NativeExtensionDeclaration {
    pub(crate) fn resource_key(&self, owner_view: u32) -> NativeExtensionResourceKey {
        NativeExtensionResourceKey {
            owner_view,
            extension_id: self.extension_id.clone(),
            component_kind: self.component_kind.clone(),
            key: self.key.clone(),
            version: self.version,
            schema_hash: self.schema_hash,
        }
    }
}

pub(crate) fn resource_key(
    owner_view: u32,
    extension_id: &str,
    component_kind: &str,
    key: &str,
    version: u32,
    schema_hash: u64,
) -> NativeExtensionResourceKey {
    NativeExtensionResourceKey {
        owner_view,
        extension_id: extension_id.to_owned().into(),
        component_kind: component_kind.to_owned().into(),
        key: key.to_owned().into(),
        version,
        schema_hash,
    }
}

pub(crate) fn declaration(node: &SnapshotNode) -> Option<NativeExtensionDeclaration> {
    let mut fields = node.data.splitn(6, '\0');
    let extension_id = fields.next()?;
    let component_kind = fields.next()?;
    let key = fields.next()?;
    let version = fields.next()?.parse().ok()?;
    let schema_hash = u64::from_str_radix(fields.next()?, 16).ok()?;
    let configuration = fields.next()?;
    Some(NativeExtensionDeclaration {
        extension_id: extension_id.into(),
        component_kind: component_kind.into(),
        key: key.into(),
        version,
        schema_hash,
        configuration: configuration.into(),
    })
}

pub(crate) fn valid_payload(payload: &[u8]) -> bool {
    let Ok(payload) = std::str::from_utf8(payload) else {
        return false;
    };
    let mut fields = payload.splitn(6, '\0');
    let Some(extension_id) = fields.next() else {
        return false;
    };
    let Some(component_kind) = fields.next() else {
        return false;
    };
    let Some(key) = fields.next() else {
        return false;
    };
    let Some(version) = fields.next() else {
        return false;
    };
    let Some(schema_hash) = fields.next() else {
        return false;
    };
    let Some(configuration) = fields.next() else {
        return false;
    };
    valid_identifier(extension_id)
        && valid_identifier(component_kind)
        && !key.is_empty()
        && !key.bytes().any(|byte| byte < 0x20)
        && version.parse::<u32>().is_ok_and(|version| version != 0)
        && schema_hash.len() == 16
        && schema_hash
            .bytes()
            .all(|byte| byte.is_ascii_digit() || matches!(byte, b'A'..=b'F'))
        && u64::from_str_radix(schema_hash, 16).is_ok_and(|hash| hash != 0)
        && !configuration.contains('\0')
}

pub(crate) fn valid_identifier(value: &str) -> bool {
    !value.is_empty()
        && value.len() <= 127
        && value
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || matches!(byte, b'.' | b'-' | b'_'))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn validates_extension_envelope() {
        assert!(valid_payload(
            b"gpui.net.test\0box\0main\01\00123456789ABCDEF\0configuration"
        ));
        assert!(!valid_payload(
            b"gpui.net.test\0box\0main\00\00123456789ABCDEF\0configuration"
        ));
        assert!(!valid_payload(
            b"gpui.net.test\0box\0main\01\00000000000000000\0configuration"
        ));
        assert!(!valid_payload(
            b"gpui.net.test\0box\0main\01\00123456789abcdef\0configuration"
        ));
    }
}
