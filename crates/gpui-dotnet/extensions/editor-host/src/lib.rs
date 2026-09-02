use std::{
    cell::{Cell, RefCell},
    rc::Rc,
    sync::Once,
};

use gpui::{
    AnyElement, App, AppContext as _, Entity, IntoElement as _, SharedString, Styled as _,
    Subscription, Window,
};
use gpui_component::input::{Editor, EditorState, InputEvent};
use gpui_dotnet::{
    abi::GpuiDotnetApiV3,
    extension::{
        NativeExtension, NativeExtensionCommand, NativeExtensionDescriptor,
        NativeExtensionEventEmitter, NativeExtensionRequest, NativeExtensionStore,
        install_native_extensions,
    },
};
use ropey::Rope;

#[path = "editor_schema.g.rs"]
mod editor_schema;

use editor_schema::{
    COMPONENT_EDITOR, EDITOR_COMMAND_BOOTSTRAP, EDITOR_FLAG_DISABLED, EDITOR_FLAG_FOLDING,
    EDITOR_EVENT_CHANGED, EDITOR_FLAG_LINE_NUMBERS, EDITOR_FLAG_READ_ONLY,
    EDITOR_FLAG_SHOW_WHITESPACE, EDITOR_KNOWN_FLAGS, EXTENSION_ID, SCHEMA_HASH, SCHEMA_VERSION,
};

struct EditorExtension;

#[derive(Clone)]
struct RetainedEditor {
    state: Entity<EditorState>,
    flags: Rc<Cell<u32>>,
    bootstrapped: Rc<Cell<bool>>,
    events: Rc<EditorEventState>,
    _subscription: Rc<Subscription>,
}

struct EditorEventState {
    token: Cell<u64>,
    revision: Cell<u64>,
    last_text: RefCell<Rope>,
    emitter: NativeExtensionEventEmitter,
    callback_error: Cell<Option<i32>>,
}

impl EditorEventState {
    fn changed(&self, current: &Rope) {
        let previous = self.last_text.replace(current.clone());
        if previous == *current {
            return;
        }
        let base_revision = self.revision.get();
        let Some(revision) = base_revision.checked_add(1) else {
            self.callback_error.set(Some(-86));
            return;
        };
        self.revision.set(revision);
        let token = self.token.get();
        if token == 0 {
            return;
        }
        let payload = encode_change(&previous, current, base_revision);
        if let Err(status) = self.emitter.emit(
            token,
            EDITOR_EVENT_CHANGED,
            0,
            revision,
            &payload,
        ) {
            self.callback_error.set(Some(status));
        }
    }
}

fn encode_change(previous: &Rope, current: &Rope, base_revision: u64) -> Vec<u8> {
    let mut prefix = 0usize;
    for (left, right) in previous.chars().zip(current.chars()) {
        if left != right {
            break;
        }
        prefix += left.len_utf8();
    }

    let mut previous_suffix = previous.chars_at(previous.len()).reversed();
    let mut current_suffix = current.chars_at(current.len()).reversed();
    let mut suffix = 0usize;
    while suffix < previous.len() - prefix && suffix < current.len() - prefix {
        let (Some(left), Some(right)) = (previous_suffix.next(), current_suffix.next()) else {
            break;
        };
        if left != right
            || suffix + left.len_utf8() > previous.len() - prefix
            || suffix + right.len_utf8() > current.len() - prefix
        {
            break;
        }
        suffix += left.len_utf8();
    }

    let deleted_length = previous.len() - prefix - suffix;
    let inserted = current.slice(prefix..current.len() - suffix);
    let mut payload = Vec::with_capacity(12 + 24 + inserted.len());
    payload.extend_from_slice(&base_revision.to_le_bytes());
    payload.extend_from_slice(&1u32.to_le_bytes());
    payload.extend_from_slice(&(prefix as u64).to_le_bytes());
    payload.extend_from_slice(&(deleted_length as u64).to_le_bytes());
    payload.extend_from_slice(&(inserted.len() as u64).to_le_bytes());
    payload.extend(inserted.bytes());
    payload
}

impl NativeExtension for EditorExtension {
    fn descriptor(&self) -> NativeExtensionDescriptor {
        NativeExtensionDescriptor {
            id: EXTENSION_ID,
            version: SCHEMA_VERSION,
            schema_hash: SCHEMA_HASH,
        }
    }

    fn validate_command(&self, command: &NativeExtensionCommand) -> bool {
        command.resource_key.extension_id() == EXTENSION_ID
            && command.resource_key.component_kind() == COMPONENT_EDITOR
            && command.command == EDITOR_COMMAND_BOOTSTRAP
            && command.flags == 0
            && command.expected_revision == 0
            && std::str::from_utf8(&command.payload).is_ok()
    }

    fn materialize(
        &self,
        request: NativeExtensionRequest,
        resources: &NativeExtensionStore,
        window: &mut Window,
        cx: &mut App,
    ) -> Result<AnyElement, SharedString> {
        if request.resource_key.component_kind() != COMPONENT_EDITOR {
            return Err("The editor host received an unknown component kind.".into());
        }
        if !request.children.is_empty() {
            return Err("Editor declarations cannot contain child elements.".into());
        }
        let Some(configuration) = EditorConfiguration::parse(&request.configuration) else {
            return Err("The editor declaration has invalid configuration.".into());
        };

        let resource = resources.get_or_insert_with(&request.resource_key, || {
            let state = cx.new(|cx| {
                EditorState::new(window, cx)
                    .language(configuration.language)
                    .line_number(configuration.flags & EDITOR_FLAG_LINE_NUMBERS != 0)
                    .folding(configuration.flags & EDITOR_FLAG_FOLDING != 0)
                    .show_whitespaces(configuration.flags & EDITOR_FLAG_SHOW_WHITESPACE != 0)
            });
            let events = Rc::new(EditorEventState {
                token: Cell::new(configuration.changed_event),
                revision: Cell::new(0),
                last_text: RefCell::new(state.read(cx).text().clone()),
                emitter: request.events,
                callback_error: Cell::new(None),
            });
            let dispatch = events.clone();
            let observed_state = state.clone();
            let subscription = window.subscribe(
                &state,
                cx,
                move |_, emitted: &InputEvent, _, cx| {
                    if matches!(emitted, InputEvent::Change) {
                        dispatch.changed(observed_state.read(cx).text());
                    }
                },
            );
            RetainedEditor {
                state,
                flags: Rc::new(Cell::new(configuration.flags)),
                bootstrapped: Rc::new(Cell::new(false)),
                events,
                _subscription: Rc::new(subscription),
            }
        });

        resource.events.token.set(configuration.changed_event);
        if let Some(status) = resource.events.callback_error.take() {
            return Err(format!("The managed editor event callback failed with status {status}.").into());
        }

        for command in request.commands {
            if command.command != EDITOR_COMMAND_BOOTSTRAP || resource.bootstrapped.replace(true) {
                return Err("The editor bootstrap command may be applied only once.".into());
            }
            let value = std::str::from_utf8(&command.payload)
                .map_err(|_| SharedString::from("The editor bootstrap payload is not UTF-8."))?
                .to_owned();
            resource
                .state
                .update(cx, |state, cx| state.set_value(value, window, cx));
            resource
                .events
                .last_text
                .replace(resource.state.read(cx).text().clone());
        }

        let previous_flags = resource.flags.replace(configuration.flags);
        if previous_flags != configuration.flags {
            resource.state.update(cx, |state, cx| {
                if previous_flags & EDITOR_FLAG_LINE_NUMBERS
                    != configuration.flags & EDITOR_FLAG_LINE_NUMBERS
                {
                    state.set_line_number(
                        configuration.flags & EDITOR_FLAG_LINE_NUMBERS != 0,
                        window,
                        cx,
                    );
                }
                if previous_flags & EDITOR_FLAG_FOLDING != configuration.flags & EDITOR_FLAG_FOLDING {
                    state.set_folding(
                        configuration.flags & EDITOR_FLAG_FOLDING != 0,
                        window,
                        cx,
                    );
                }
                if previous_flags & EDITOR_FLAG_SHOW_WHITESPACE
                    != configuration.flags & EDITOR_FLAG_SHOW_WHITESPACE
                {
                    state.set_show_whitespaces(
                        configuration.flags & EDITOR_FLAG_SHOW_WHITESPACE != 0,
                        window,
                        cx,
                    );
                }
            });
        }

        Ok(Editor::new(&resource.state)
            .disabled(configuration.flags & EDITOR_FLAG_DISABLED != 0)
            .readonly(configuration.flags & EDITOR_FLAG_READ_ONLY != 0)
            .size_full()
            .into_any_element())
    }
}

struct EditorConfiguration<'a> {
    flags: u32,
    language: &'a str,
    changed_event: u64,
}

impl<'a> EditorConfiguration<'a> {
    fn parse(value: &'a str) -> Option<Self> {
        let mut fields = value.splitn(3, '\n');
        let flags = fields.next()?.parse::<u32>().ok()?;
        let language = fields.next()?;
        let changed_event = fields.next()?.parse::<u64>().ok()?;
        if flags & !EDITOR_KNOWN_FLAGS != 0 {
            return None;
        }
        Some(Self {
            flags,
            language,
            changed_event,
        })
    }
}

static EDITOR_EXTENSION: EditorExtension = EditorExtension;
static EXTENSIONS: [&dyn NativeExtension; 1] = [&EDITOR_EXTENSION];
static INSTALL: Once = Once::new();

#[unsafe(no_mangle)]
pub extern "C" fn gpui_dotnet_get_api(requested_version: u32) -> *const GpuiDotnetApiV3 {
    INSTALL.call_once(|| {
        install_native_extensions(&EXTENSIONS)
            .expect("the editor host must install its extension registry exactly once");
    });
    gpui_dotnet::api(requested_version)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_editor_configuration() {
        let configuration = EditorConfiguration::parse("12\nrust\n42").unwrap();
        assert_eq!(
            configuration.flags,
            EDITOR_FLAG_LINE_NUMBERS | EDITOR_FLAG_FOLDING
        );
        assert_eq!(configuration.language, "rust");
        assert_eq!(configuration.changed_event, 42);
        assert!(EditorConfiguration::parse("32\nrust\n42").is_none());
        assert!(EditorConfiguration::parse("12\nrust").is_none());
    }

    #[test]
    fn encodes_one_minimal_utf8_change() {
        let payload = encode_change(&Rope::from("a🙂z"), &Rope::from("a界z"), 7);
        assert_eq!(u64::from_le_bytes(payload[0..8].try_into().unwrap()), 7);
        assert_eq!(u32::from_le_bytes(payload[8..12].try_into().unwrap()), 1);
        assert_eq!(u64::from_le_bytes(payload[12..20].try_into().unwrap()), 1);
        assert_eq!(u64::from_le_bytes(payload[20..28].try_into().unwrap()), 4);
        assert_eq!(u64::from_le_bytes(payload[28..36].try_into().unwrap()), 3);
        assert_eq!(&payload[36..], "界".as_bytes());
    }

    #[test]
    fn custom_host_advertises_editor_schema() {
        let api = gpui_dotnet_get_api(3);
        assert!(!api.is_null());
        let api = unsafe { &*api };
        let supports = api.supports_extension.unwrap();
        let id = EXTENSION_ID.as_bytes();
        assert_eq!(
            unsafe { supports(id.as_ptr(), id.len() as i32, SCHEMA_VERSION, SCHEMA_HASH) },
            0
        );
        assert_eq!(
            unsafe { supports(id.as_ptr(), id.len() as i32, SCHEMA_VERSION, SCHEMA_HASH + 1) },
            -82
        );
    }

    #[test]
    fn custom_host_validates_editor_commands_before_view_routing() {
        let api = gpui_dotnet_get_api(3);
        let api = unsafe { &*api };
        let dispatch = api.dispatch_extension_command.unwrap();
        let extension_id = EXTENSION_ID.as_bytes();
        let component_kind = COMPONENT_EDITOR.as_bytes();
        let key = b"document";
        let payload = b"hello\0world";
        let mut command = gpui_dotnet::abi::NativeExtensionCommand {
            owner_view: 7,
            command: EDITOR_COMMAND_BOOTSTRAP,
            flags: 0,
            schema_version: SCHEMA_VERSION,
            reserved: 0,
            schema_hash: SCHEMA_HASH,
            expected_revision: 0,
            extension_id: extension_id.as_ptr(),
            extension_id_length: extension_id.len() as i32,
            component_kind: component_kind.as_ptr(),
            component_kind_length: component_kind.len() as i32,
            key: key.as_ptr(),
            key_length: key.len() as i32,
            payload: payload.as_ptr(),
            payload_length: payload.len() as i32,
        };

        assert_eq!(unsafe { dispatch(u64::MAX - 1, &command) }, -30);
        command.command = 99;
        assert_eq!(unsafe { dispatch(u64::MAX - 1, &command) }, -85);
    }
}
