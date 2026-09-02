use std::{cell::Cell, rc::Rc, sync::Once};

use gpui::{AnyElement, App, AppContext as _, Entity, IntoElement as _, SharedString, Styled as _, Window};
use gpui_component::input::{Editor, EditorState};
use gpui_dotnet::{
    abi::GpuiDotnetApiV3,
    extension::{
        NativeExtension, NativeExtensionCommand, NativeExtensionDescriptor, NativeExtensionRequest,
        NativeExtensionStore, install_native_extensions,
    },
};

#[path = "editor_schema.g.rs"]
mod editor_schema;

use editor_schema::{
    COMPONENT_EDITOR, EDITOR_COMMAND_BOOTSTRAP, EDITOR_FLAG_DISABLED, EDITOR_FLAG_FOLDING,
    EDITOR_FLAG_LINE_NUMBERS, EDITOR_FLAG_READ_ONLY, EDITOR_FLAG_SHOW_WHITESPACE,
    EDITOR_KNOWN_FLAGS, EXTENSION_ID, SCHEMA_HASH, SCHEMA_VERSION,
};

struct EditorExtension;

#[derive(Clone)]
struct RetainedEditor {
    state: Entity<EditorState>,
    flags: Rc<Cell<u32>>,
    bootstrapped: Rc<Cell<bool>>,
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
            RetainedEditor {
                state,
                flags: Rc::new(Cell::new(configuration.flags)),
                bootstrapped: Rc::new(Cell::new(false)),
            }
        });

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
}

impl<'a> EditorConfiguration<'a> {
    fn parse(value: &'a str) -> Option<Self> {
        let mut fields = value.splitn(2, '\n');
        let flags = fields.next()?.parse::<u32>().ok()?;
        let language = fields.next()?;
        if flags & !EDITOR_KNOWN_FLAGS != 0 {
            return None;
        }
        Some(Self { flags, language })
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
        let configuration = EditorConfiguration::parse("12\nrust").unwrap();
        assert_eq!(
            configuration.flags,
            EDITOR_FLAG_LINE_NUMBERS | EDITOR_FLAG_FOLDING
        );
        assert_eq!(configuration.language, "rust");
        assert!(EditorConfiguration::parse("32\nrust").is_none());
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
