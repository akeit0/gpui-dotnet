use std::{
    cell::{Cell, RefCell},
    rc::Rc,
    sync::Once,
};

use gpui::{
    AnyElement, App, AppContext as _, Entity, IntoElement as _, SharedString, Styled as _,
    Subscription, Window, px,
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
    COMPONENT_EDITOR, EDITOR_COMMAND_APPLY_EDIT, EDITOR_COMMAND_BOOTSTRAP, EDITOR_COMMAND_FOCUS,
    EDITOR_COMMAND_REPLACE_DOCUMENT, EDITOR_COMMAND_SET_SELECTION, EDITOR_EVENT_CHANGED,
    EDITOR_EVENT_COMMAND_REJECTED, EDITOR_FLAG_DISABLED, EDITOR_FLAG_FOLDING,
    EDITOR_FLAG_LINE_NUMBERS, EDITOR_FLAG_READ_ONLY, EDITOR_FLAG_SHOW_WHITESPACE,
    EDITOR_KNOWN_FLAGS, EXTENSION_ID, SCHEMA_HASH, SCHEMA_VERSION,
};

const EDITOR_CHANGE_ORIGIN_USER: u16 = 0;
const EDITOR_CHANGE_ORIGIN_COMMAND: u16 = 1;
const EDITOR_REJECTION_STALE_REVISION: u16 = 1;
const EDITOR_REJECTION_INVALID_RANGE: u16 = 2;

struct EditorExtension;

#[derive(Clone)]
struct RetainedEditor {
    state: Entity<EditorState>,
    flags: Rc<Cell<u32>>,
    line_number_width: Rc<Cell<Option<f32>>>,
    bootstrapped: Rc<Cell<bool>>,
    events: Rc<EditorEventState>,
    _subscription: Rc<Subscription>,
}

struct EditorEventState {
    changed_token: Cell<u64>,
    rejected_token: Cell<u64>,
    revision: Cell<u64>,
    last_text: RefCell<Rope>,
    next_origin: Cell<u16>,
    emitter: NativeExtensionEventEmitter,
    callback_error: Cell<Option<i32>>,
}

impl EditorEventState {
    fn changed(&self, current: &Rope) {
        let origin = self.next_origin.replace(EDITOR_CHANGE_ORIGIN_USER);
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
        let token = self.changed_token.get();
        if token == 0 {
            return;
        }
        let payload = encode_change(&previous, current, base_revision);
        if let Err(status) = self.emitter.emit(
            token,
            EDITOR_EVENT_CHANGED,
            origin,
            revision,
            &payload,
        ) {
            self.callback_error.set(Some(status));
        }
    }

    fn command_rejected(&self, command: u16, reason: u16, expected_revision: u64) {
        let token = self.rejected_token.get();
        if token == 0 {
            return;
        }
        let mut payload = [0; 12];
        payload[..2].copy_from_slice(&command.to_le_bytes());
        payload[4..].copy_from_slice(&expected_revision.to_le_bytes());
        if let Err(status) = self.emitter.emit(
            token,
            EDITOR_EVENT_COMMAND_REJECTED,
            reason,
            self.revision.get(),
            &payload,
        ) {
            self.callback_error.set(Some(status));
        }
    }
}

fn read_u64(payload: &[u8], offset: usize) -> Option<u64> {
    Some(u64::from_le_bytes(
        payload.get(offset..offset.checked_add(8)?)?.try_into().ok()?,
    ))
}

fn valid_utf8_edit(payload: &[u8]) -> bool {
    payload.len() >= 16
        && read_u64(payload, 0)
            .zip(read_u64(payload, 8))
            .is_some_and(|(start, deleted)| start.checked_add(deleted).is_some())
        && std::str::from_utf8(&payload[16..]).is_ok()
}

fn byte_range(text: &Rope, start: u64, end: u64) -> Option<std::ops::Range<usize>> {
    let start = usize::try_from(start).ok()?;
    let end = usize::try_from(end).ok()?;
    (start <= end
        && end <= text.len()
        && text.is_char_boundary(start)
        && text.is_char_boundary(end))
    .then_some(start..end)
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
        if command.resource_key.extension_id() != EXTENSION_ID
            || command.resource_key.component_kind() != COMPONENT_EDITOR
            || command.flags != 0
        {
            return false;
        }
        match command.command {
            EDITOR_COMMAND_BOOTSTRAP => {
                command.expected_revision == 0 && std::str::from_utf8(&command.payload).is_ok()
            }
            EDITOR_COMMAND_FOCUS => command.expected_revision == 0 && command.payload.is_empty(),
            EDITOR_COMMAND_SET_SELECTION => command.payload.len() == 16,
            EDITOR_COMMAND_REPLACE_DOCUMENT => std::str::from_utf8(&command.payload).is_ok(),
            EDITOR_COMMAND_APPLY_EDIT => valid_utf8_edit(&command.payload),
            _ => false,
        }
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
                    .line_number_width(configuration.line_number_width.map(px))
                    .folding(configuration.flags & EDITOR_FLAG_FOLDING != 0)
                    .show_whitespaces(configuration.flags & EDITOR_FLAG_SHOW_WHITESPACE != 0)
            });
            let events = Rc::new(EditorEventState {
                changed_token: Cell::new(configuration.changed_event),
                rejected_token: Cell::new(configuration.command_rejected_event),
                revision: Cell::new(0),
                last_text: RefCell::new(state.read(cx).text().clone()),
                next_origin: Cell::new(EDITOR_CHANGE_ORIGIN_USER),
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
                line_number_width: Rc::new(Cell::new(configuration.line_number_width)),
                bootstrapped: Rc::new(Cell::new(false)),
                events,
                _subscription: Rc::new(subscription),
            }
        });

        resource
            .events
            .changed_token
            .set(configuration.changed_event);
        resource
            .events
            .rejected_token
            .set(configuration.command_rejected_event);
        if let Some(status) = resource.events.callback_error.take() {
            return Err(format!("The managed editor event callback failed with status {status}.").into());
        }

        for command in request.commands {
            if command.command == EDITOR_COMMAND_BOOTSTRAP {
                if resource.bootstrapped.replace(true) {
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
                continue;
            }

            if command.command == EDITOR_COMMAND_FOCUS {
                resource
                    .state
                    .update(cx, |state, cx| state.focus(window, cx));
                continue;
            }

            if command.expected_revision != resource.events.revision.get() {
                resource.events.command_rejected(
                    command.command,
                    EDITOR_REJECTION_STALE_REVISION,
                    command.expected_revision,
                );
                continue;
            }

            let range = match command.command {
                EDITOR_COMMAND_SET_SELECTION => read_u64(&command.payload, 0)
                    .zip(read_u64(&command.payload, 8))
                    .and_then(|(start, end)| byte_range(resource.state.read(cx).text(), start, end)),
                EDITOR_COMMAND_APPLY_EDIT => read_u64(&command.payload, 0)
                    .zip(read_u64(&command.payload, 8))
                    .and_then(|(start, deleted)| {
                        start.checked_add(deleted).and_then(|end| {
                            byte_range(resource.state.read(cx).text(), start, end)
                        })
                    }),
                EDITOR_COMMAND_REPLACE_DOCUMENT => Some(0..0),
                _ => return Err("The editor host received an unknown command.".into()),
            };
            let Some(range) = range else {
                resource.events.command_rejected(
                    command.command,
                    EDITOR_REJECTION_INVALID_RANGE,
                    command.expected_revision,
                );
                continue;
            };

            resource
                .events
                .next_origin
                .set(EDITOR_CHANGE_ORIGIN_COMMAND);
            match command.command {
                EDITOR_COMMAND_SET_SELECTION => {
                    resource
                        .state
                        .update(cx, |state, cx| state.set_selected_range(range, cx));
                    resource
                        .events
                        .next_origin
                        .set(EDITOR_CHANGE_ORIGIN_USER);
                }
                EDITOR_COMMAND_REPLACE_DOCUMENT => {
                    let value = std::str::from_utf8(&command.payload)
                        .map_err(|_| {
                            SharedString::from("The editor replacement payload is not UTF-8.")
                        })?
                        .to_owned();
                    resource
                        .state
                        .update(cx, |state, cx| state.replace_all(value, window, cx));
                    resource.events.changed(resource.state.read(cx).text());
                }
                EDITOR_COMMAND_APPLY_EDIT => {
                    let value = std::str::from_utf8(&command.payload[16..])
                        .map_err(|_| SharedString::from("The editor edit payload is not UTF-8."))?
                        .to_owned();
                    resource.state.update(cx, |state, cx| {
                        state.set_selected_range(range, cx);
                        state.replace(value, window, cx);
                    });
                    resource.events.changed(resource.state.read(cx).text());
                }
                _ => return Err("The editor host received an unknown command.".into()),
            }
        }

        if let Some(status) = resource.events.callback_error.take() {
            return Err(format!("The managed editor event callback failed with status {status}.").into());
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

        if resource
            .line_number_width
            .replace(configuration.line_number_width)
            != configuration.line_number_width
        {
            resource.state.update(cx, |state, cx| {
                state.set_line_number_width(
                    configuration.line_number_width.map(px),
                    window,
                    cx,
                );
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
    command_rejected_event: u64,
    line_number_width: Option<f32>,
}

impl<'a> EditorConfiguration<'a> {
    fn parse(value: &'a str) -> Option<Self> {
        let mut fields = value.splitn(5, '\n');
        let flags = fields.next()?.parse::<u32>().ok()?;
        let language = fields.next()?;
        let changed_event = fields.next()?.parse::<u64>().ok()?;
        let command_rejected_event = fields.next()?.parse::<u64>().ok()?;
        let line_number_width = fields.next()?.parse::<f32>().ok()?;
        if flags & !EDITOR_KNOWN_FLAGS != 0
            || !line_number_width.is_finite()
            || line_number_width < 0.
        {
            return None;
        }
        Some(Self {
            flags,
            language,
            changed_event,
            command_rejected_event,
            line_number_width: (line_number_width > 0.).then_some(line_number_width),
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
        let configuration = EditorConfiguration::parse("12\nrust\n42\n43\n64").unwrap();
        assert_eq!(
            configuration.flags,
            EDITOR_FLAG_LINE_NUMBERS | EDITOR_FLAG_FOLDING
        );
        assert_eq!(configuration.language, "rust");
        assert_eq!(configuration.changed_event, 42);
        assert_eq!(configuration.command_rejected_event, 43);
        assert_eq!(configuration.line_number_width, Some(64.));
        assert_eq!(
            EditorConfiguration::parse("12\nrust\n42\n43\n0")
                .unwrap()
                .line_number_width,
            None
        );
        assert!(EditorConfiguration::parse("32\nrust\n42\n43\n64").is_none());
        assert!(EditorConfiguration::parse("12\nrust\n42\n43\n-1").is_none());
        assert!(EditorConfiguration::parse("12\nrust\n42").is_none());
    }

    #[test]
    fn custom_host_bundles_rust_highlighting_and_allows_plain_text_fallback() {
        let registry = gpui_component::highlighter::LanguageRegistry::singleton();
        assert!(
            registry
                .language("rust")
                .is_some_and(|language| language.has_grammar())
        );
        assert!(registry.language("not-bundled").is_none());
    }

    #[test]
    fn validates_edit_payload_and_utf8_byte_ranges() {
        let mut payload = Vec::new();
        payload.extend_from_slice(&1u64.to_le_bytes());
        payload.extend_from_slice(&3u64.to_le_bytes());
        payload.extend_from_slice("界".as_bytes());
        assert!(valid_utf8_edit(&payload));
        payload.push(0xFF);
        assert!(!valid_utf8_edit(&payload));

        let text = Rope::from("a界z");
        assert_eq!(byte_range(&text, 1, 4), Some(1..4));
        assert_eq!(byte_range(&text, 2, 4), None);
        assert_eq!(byte_range(&text, 0, 6), None);
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
