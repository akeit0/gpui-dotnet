use gpui::{App, FocusHandle, Pixels, Styled, Window};

#[cfg(target_os = "ios")]
use gpui::Point;

pub(crate) trait StyledCompat: Styled + Sized {
    #[cfg(target_os = "ios")]
    fn flex_grow_compat(self) -> Self {
        self.flex_grow(1.0)
    }

    #[cfg(not(target_os = "ios"))]
    fn flex_grow_compat(self) -> Self {
        self.flex_grow()
    }
}

impl<T: Styled> StyledCompat for T {}

#[cfg(target_os = "ios")]
pub(crate) fn focus(handle: &FocusHandle, window: &mut Window, cx: &mut App) {
    handle.focus(window, cx);
}

#[cfg(not(target_os = "ios"))]
pub(crate) fn focus(handle: &FocusHandle, window: &mut Window, _: &mut App) {
    handle.focus(window);
}

#[cfg(target_os = "ios")]
pub(crate) fn blur(window: &mut Window, cx: &mut App) {
    window.blur(cx);
}

#[cfg(not(target_os = "ios"))]
pub(crate) fn blur(window: &mut Window, _: &mut App) {
    window.blur();
}

#[cfg(target_os = "ios")]
pub(crate) fn focus_next(window: &mut Window, cx: &mut App) {
    window.focus_next(cx);
}

#[cfg(not(target_os = "ios"))]
pub(crate) fn focus_next(window: &mut Window, _: &mut App) {
    window.focus_next();
}

#[cfg(target_os = "ios")]
pub(crate) fn focus_prev(window: &mut Window, cx: &mut App) {
    window.focus_prev(cx);
}

#[cfg(not(target_os = "ios"))]
pub(crate) fn focus_prev(window: &mut Window, _: &mut App) {
    window.focus_prev();
}

#[cfg(target_os = "ios")]
pub(crate) fn max_offset_width(offset: Point<Pixels>) -> Pixels {
    offset.x
}

#[cfg(not(target_os = "ios"))]
pub(crate) fn max_offset_width(offset: gpui::Size<Pixels>) -> Pixels {
    offset.width
}

#[cfg(target_os = "ios")]
pub(crate) fn max_offset_height(offset: Point<Pixels>) -> Pixels {
    offset.y
}

#[cfg(not(target_os = "ios"))]
pub(crate) fn max_offset_height(offset: gpui::Size<Pixels>) -> Pixels {
    offset.height
}
