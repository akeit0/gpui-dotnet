use std::{cell::RefCell, rc::Rc};

use gpui::{App, Hsla, rgba};
use gpui_base::{ScrollbarStyles, ThemeAppearance};

use crate::abi::NativeThemePayload;

pub(crate) const NATIVE_THEME_VERSION: u32 = 2;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub(crate) enum NativeThemeAppearance {
    #[default]
    Light,
    Dark,
}

impl NativeThemeAppearance {
    fn from_wire(value: u32) -> Option<Self> {
        match value {
            0 => Some(Self::Light),
            1 => Some(Self::Dark),
            _ => None,
        }
    }

    fn to_base(self) -> ThemeAppearance {
        match self {
            Self::Light => ThemeAppearance::Light,
            Self::Dark => ThemeAppearance::Dark,
        }
    }
}

#[derive(Clone, Copy, Debug)]
pub(crate) struct NativeTheme {
    pub(crate) appearance: NativeThemeAppearance,
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
        if payload.version != NATIVE_THEME_VERSION {
            return None;
        }

        Some(Self {
            appearance: NativeThemeAppearance::from_wire(payload.appearance)?,
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

    pub(crate) fn apply(self, cx: &mut App) {
        self.project_to_base(gpui_base::Theme::global_mut(cx));
    }

    pub(crate) fn resolved(self) -> crate::extension::ResolvedTheme {
        crate::extension::ResolvedTheme {
            dark: self.appearance == NativeThemeAppearance::Dark,
            background: self.background,
            text: self.text,
            text_muted: self.text_muted,
            text_placeholder: self.text_placeholder,
            text_on_accent: self.text_on_accent,
            border: self.border,
            border_variant: self.border_variant,
            border_focused: self.border_focused,
            surface_background: self.surface_background,
            element_background: self.element_background,
            element_hover: self.element_hover,
            element_active: self.element_active,
            accent: self.accent,
            info: self.info,
            info_background: self.info_background,
            error: self.error,
            scrollbar_thumb_background: self.scrollbar_thumb_background,
            scrollbar_track_background: self.scrollbar_track_background,
        }
    }

    fn project_to_base(self, theme: &mut gpui_base::Theme) {
        theme.appearance = self.appearance.to_base();

        let background = color(self.background);
        let text = color(self.text);
        let text_muted = color(self.text_muted);
        let text_on_accent = color(self.text_on_accent);
        let border = color(self.border);
        let border_variant = color(self.border_variant);
        let border_focused = color(self.border_focused);
        let surface = color(self.surface_background);
        let element = color(self.element_background);
        let element_hover = color(self.element_hover);
        let primary = color(self.accent);
        let destructive = color(self.error);

        let colors = &mut theme.tokens.colors;
        colors.background = background;
        colors.foreground = text;
        colors.surface = surface;
        colors.surface_foreground = text;
        colors.primary = primary;
        colors.primary_foreground = text_on_accent;
        colors.secondary = element;
        colors.secondary_foreground = text;
        colors.muted = element;
        colors.muted_foreground = text_muted;
        colors.accent = element_hover;
        colors.accent_foreground = text;
        colors.destructive = destructive;
        colors.destructive_foreground = text_on_accent;
        colors.border = border;
        colors.input = border_variant;
        colors.ring = border_focused;
        colors.selection = primary.alpha(0.3);

        theme.resizable.handle = Some(border);
        theme.resizable.active_handle = Some(border_focused);
        theme.scrollbar = theme.scrollbar.clone().with_styles(
            ScrollbarStyles::default()
                .track(|style| style.bg(color(self.scrollbar_track_background)))
                .thumb(|style| style.bg(color(self.scrollbar_thumb_background))),
        );
    }
}

fn color(value: u32) -> Hsla {
    rgba(value).into()
}

impl Default for NativeTheme {
    fn default() -> Self {
        Self {
            appearance: NativeThemeAppearance::Light,
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
            appearance: 1,
            background: 0x01020304,
            accent: 0xAABBCCDD,
            ..Default::default()
        };
        let theme = NativeTheme::from_payload(payload).expect("current payload is accepted");
        assert_eq!(std::mem::size_of::<NativeThemePayload>(), 20 * 4);
        assert_eq!(theme.appearance, NativeThemeAppearance::Dark);
        assert_eq!(theme.background, 0x01020304);
        assert_eq!(theme.accent, 0xAABBCCDD);

        let stale = NativeThemePayload {
            version: NATIVE_THEME_VERSION + 1,
            ..payload
        };
        assert!(NativeTheme::from_payload(stale).is_none());

        let invalid_appearance = NativeThemePayload {
            appearance: 2,
            ..payload
        };
        assert!(NativeTheme::from_payload(invalid_appearance).is_none());
    }

    #[gpui::test]
    fn projects_managed_roles_into_the_foundation_theme(cx: &mut gpui::TestAppContext) {
        cx.update(|cx| {
            gpui_base::init(cx);
            let defaults = gpui_base::Theme::global(cx);
            let typography = defaults.tokens.typography;
            let radius = defaults.tokens.radius;
            let spacing = defaults.tokens.spacing;
            let shadow = defaults.tokens.shadow;

            let theme = NativeTheme {
                appearance: NativeThemeAppearance::Dark,
                background: 0x101820FF,
                text: 0xF0F4F8FF,
                text_muted: 0xA0A8B0FF,
                text_on_accent: 0xFFFFFFFF,
                border: 0x303840FF,
                border_variant: 0x404850FF,
                border_focused: 0x6688FFFF,
                surface_background: 0x182028FF,
                element_background: 0x202830FF,
                element_hover: 0x283038FF,
                element_active: 0x303840FF,
                accent: 0x4466EEFF,
                info: 0x2288CCFF,
                info_background: 0x183848FF,
                error: 0xCC3344FF,
                text_placeholder: 0x707880FF,
                scrollbar_thumb_background: 0x8090A0FF,
                scrollbar_track_background: 0x10182080,
            };
            theme.apply(cx);

            let projected = gpui_base::Theme::global(cx);
            assert_eq!(projected.appearance, ThemeAppearance::Dark);
            assert_eq!(projected.tokens.colors.background, color(theme.background));
            assert_eq!(projected.tokens.colors.foreground, color(theme.text));
            assert_eq!(
                projected.tokens.colors.surface,
                color(theme.surface_background)
            );
            assert_eq!(projected.tokens.colors.primary, color(theme.accent));
            assert_eq!(
                projected.tokens.colors.primary_foreground,
                color(theme.text_on_accent)
            );
            assert_eq!(projected.tokens.colors.border, color(theme.border));
            assert_eq!(projected.tokens.colors.input, color(theme.border_variant));
            assert_eq!(projected.tokens.colors.ring, color(theme.border_focused));
            assert_eq!(projected.resizable.handle, Some(color(theme.border)));
            assert_eq!(
                projected.resizable.active_handle,
                Some(color(theme.border_focused))
            );
            assert_eq!(projected.tokens.typography, typography);
            assert_eq!(projected.tokens.radius, radius);
            assert_eq!(projected.tokens.spacing, spacing);
            assert_eq!(projected.tokens.shadow, shadow);

            use gpui::{IntoElement as _, ParentElement as _, Styled as _};
            let _: gpui::AnyElement = gpui_base::Button::new("managed-theme-probe")
                .bg(projected.tokens.colors.primary)
                .text_color(projected.tokens.colors.primary_foreground)
                .child("Probe")
                .into_any_element();
        });
    }
}
