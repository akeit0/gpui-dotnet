use std::{cell::Cell, rc::Rc, sync::Once};

use gpui::{AnyElement, App, AppContext as _, Entity, IntoElement as _, SharedString, Styled as _, Window};
use gpui_component::input::{Editor, EditorState};
use gpui_dotnet::{
    abi::GpuiDotnetApiV2,
    extension::{
        NativeExtension, NativeExtensionDescriptor, NativeExtensionRequest, NativeExtensionStore,
        install_native_extensions,
    },
};

const EDITOR_SCHEMA_HASH: u64 = 0x556347593588921F;
const FLAG_DISABLED: u32 = 1 << 0;
const FLAG_READ_ONLY: u32 = 1 << 1;
const FLAG_LINE_NUMBERS: u32 = 1 << 2;
const FLAG_FOLDING: u32 = 1 << 3;
const FLAG_SHOW_WHITESPACE: u32 = 1 << 4;
const KNOWN_FLAGS: u32 = FLAG_DISABLED
    | FLAG_READ_ONLY
    | FLAG_LINE_NUMBERS
    | FLAG_FOLDING
    | FLAG_SHOW_WHITESPACE;

struct EditorExtension;

#[derive(Clone)]
struct RetainedEditor {
    state: Entity<EditorState>,
    flags: Rc<Cell<u32>>,
}

impl NativeExtension for EditorExtension {
    fn descriptor(&self) -> NativeExtensionDescriptor {
        NativeExtensionDescriptor {
            id: "gpui.net.editor",
            version: 1,
            schema_hash: EDITOR_SCHEMA_HASH,
        }
    }

    fn materialize(
        &self,
        request: NativeExtensionRequest,
        resources: &NativeExtensionStore,
        window: &mut Window,
        cx: &mut App,
    ) -> Result<AnyElement, SharedString> {
        if request.resource_key.component_kind() != "editor" {
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
                    .line_number(configuration.flags & FLAG_LINE_NUMBERS != 0)
                    .folding(configuration.flags & FLAG_FOLDING != 0)
                    .show_whitespaces(configuration.flags & FLAG_SHOW_WHITESPACE != 0)
                    .default_value(configuration.initial_value)
            });
            RetainedEditor {
                state,
                flags: Rc::new(Cell::new(configuration.flags)),
            }
        });

        let previous_flags = resource.flags.replace(configuration.flags);
        if previous_flags != configuration.flags {
            resource.state.update(cx, |state, cx| {
                if previous_flags & FLAG_LINE_NUMBERS != configuration.flags & FLAG_LINE_NUMBERS {
                    state.set_line_number(
                        configuration.flags & FLAG_LINE_NUMBERS != 0,
                        window,
                        cx,
                    );
                }
                if previous_flags & FLAG_FOLDING != configuration.flags & FLAG_FOLDING {
                    state.set_folding(configuration.flags & FLAG_FOLDING != 0, window, cx);
                }
                if previous_flags & FLAG_SHOW_WHITESPACE
                    != configuration.flags & FLAG_SHOW_WHITESPACE
                {
                    state.set_show_whitespaces(
                        configuration.flags & FLAG_SHOW_WHITESPACE != 0,
                        window,
                        cx,
                    );
                }
            });
        }

        Ok(Editor::new(&resource.state)
            .disabled(configuration.flags & FLAG_DISABLED != 0)
            .readonly(configuration.flags & FLAG_READ_ONLY != 0)
            .size_full()
            .into_any_element())
    }
}

struct EditorConfiguration<'a> {
    flags: u32,
    language: &'a str,
    initial_value: &'a str,
}

impl<'a> EditorConfiguration<'a> {
    fn parse(value: &'a str) -> Option<Self> {
        let mut fields = value.splitn(3, '\n');
        let flags = fields.next()?.parse::<u32>().ok()?;
        let language = fields.next()?;
        let initial_value = fields.next()?;
        if flags & !KNOWN_FLAGS != 0 {
            return None;
        }
        Some(Self {
            flags,
            language,
            initial_value,
        })
    }
}

static EDITOR_EXTENSION: EditorExtension = EditorExtension;
static EXTENSIONS: [&dyn NativeExtension; 1] = [&EDITOR_EXTENSION];
static INSTALL: Once = Once::new();

#[unsafe(no_mangle)]
pub extern "C" fn gpui_dotnet_get_api(requested_version: u32) -> *const GpuiDotnetApiV2 {
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
        let configuration = EditorConfiguration::parse("12\nrust\nfn main() {}\n").unwrap();
        assert_eq!(configuration.flags, FLAG_LINE_NUMBERS | FLAG_FOLDING);
        assert_eq!(configuration.language, "rust");
        assert_eq!(configuration.initial_value, "fn main() {}\n");
        assert!(EditorConfiguration::parse("32\nrust\n").is_none());
    }

    #[test]
    fn custom_host_advertises_editor_schema() {
        let api = gpui_dotnet_get_api(2);
        assert!(!api.is_null());
        let api = unsafe { &*api };
        let supports = api.supports_extension.unwrap();
        let id = b"gpui.net.editor";
        assert_eq!(
            unsafe { supports(id.as_ptr(), id.len() as i32, 1, EDITOR_SCHEMA_HASH) },
            0
        );
        assert_eq!(
            unsafe { supports(id.as_ptr(), id.len() as i32, 1, EDITOR_SCHEMA_HASH + 1) },
            -82
        );
    }
}
