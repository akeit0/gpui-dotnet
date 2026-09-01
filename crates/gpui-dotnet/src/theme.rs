use std::{cell::RefCell, rc::Rc};

use crate::abi::NativeThemePayload;

pub(crate) const NATIVE_THEME_VERSION: u32 = 1;

#[derive(Clone, Copy, Debug)]
pub(crate) struct NativeTheme {
    pub(crate) background: u32,
    pub(crate) text: u32,
    pub(crate) text_muted: u32,
    pub(crate) text_placeholder: u32,
    pub(crate) text_on_accent: u32,
    pub(crate) border: u32,
    pub(crate) border_variant: u32,
    pub(crate) border_focused: u32,
    pub(crate) surface_background: u32,
    pub(crate) element_background: u32,
    pub(crate) element_hover: u32,
    pub(crate) element_active: u32,
    pub(crate) accent: u32,
    pub(crate) info: u32,
    pub(crate) info_background: u32,
    pub(crate) error: u32,
    pub(crate) scrollbar_thumb_background: u32,
    pub(crate) scrollbar_track_background: u32,
}

pub(crate) type SharedTheme = Rc<RefCell<NativeTheme>>;

impl NativeTheme {
    pub(crate) fn from_payload(payload: NativeThemePayload) -> Option<Self> {
        (payload.version == NATIVE_THEME_VERSION).then_some(Self {
            background: payload.background,
            text: payload.text,
            text_muted: payload.text_muted,
            text_placeholder: payload.text_placeholder,
            text_on_accent: payload.text_on_accent,
            border: payload.border,
            border_variant: payload.border_variant,
            border_focused: payload.border_focused,
            surface_background: payload.surface_background,
            element_background: payload.element_background,
            element_hover: payload.element_hover,
            element_active: payload.element_active,
            accent: payload.accent,
            info: payload.info,
            info_background: payload.info_background,
            error: payload.error,
            scrollbar_thumb_background: payload.scrollbar_thumb_background,
            scrollbar_track_background: payload.scrollbar_track_background,
        })
    }
}

impl Default for NativeTheme {
    fn default() -> Self {
        Self {
            background: 0xF8FAFCFF,
            text: 0x0F172AFF,
            text_muted: 0x64748BFF,
            text_placeholder: 0x94A3B8FF,
            text_on_accent: 0xFFFFFFFF,
            border: 0xCBD5E1FF,
            border_variant: 0xE2E8F0FF,
            border_focused: 0x818CF8FF,
            surface_background: 0xFFFFFFFF,
            element_background: 0xFFFFFFFF,
            element_hover: 0xF1F5F9FF,
            element_active: 0xE2E8F0FF,
            accent: 0x4F46E5FF,
            info: 0x1D4ED8FF,
            info_background: 0xDBEAFEFF,
            error: 0xB91C1CFF,
            scrollbar_thumb_background: 0x94A3B8FF,
            scrollbar_track_background: 0x00000000,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn payload_version_is_validated_and_semantic_values_are_preserved() {
        let payload = NativeThemePayload {
            version: NATIVE_THEME_VERSION,
            background: 0x01020304,
            accent: 0xAABBCCDD,
            ..Default::default()
        };
        let theme = NativeTheme::from_payload(payload).expect("current payload is accepted");
        assert_eq!(std::mem::size_of::<NativeThemePayload>(), 19 * 4);
        assert_eq!(theme.background, 0x01020304);
        assert_eq!(theme.accent, 0xAABBCCDD);

        let stale = NativeThemePayload {
            version: NATIVE_THEME_VERSION + 1,
            ..payload
        };
        assert!(NativeTheme::from_payload(stale).is_none());
    }
}
