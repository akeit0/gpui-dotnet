use gpui::{
    BoxShadow, InteractiveElement, StatefulInteractiveElement, StyleRefinement, Styled, px, rgba,
};

/// Focus is a paint layer: never replace application borders or change control geometry.
pub(crate) fn focus_ring<T: InteractiveElement + Styled>(mut element: T, color: u32) -> T {
    let focus = focus_ring_style(element.style(), color);
    element.focus_visible(move |_| focus)
}

fn focus_ring_style(base: &StyleRefinement, color: u32) -> StyleRefinement {
    let mut shadows = base.box_shadow.clone().unwrap_or_default();
    shadows.push(BoxShadow::new(px(0.), px(0.), rgba(color).into()).spread_radius(px(2.)));
    StyleRefinement::default().shadow(shadows)
}

/// Disabled attenuation composes with authored opacity, including an explicitly hidden control.
pub(crate) fn disabled<T: Styled>(mut element: T, disabled: bool) -> T {
    if disabled {
        let opacity = attenuated_opacity(element.style(), 0.5);
        element = element.opacity(opacity);
    }
    element
}

pub(crate) fn pressed_feedback<T: StatefulInteractiveElement + Styled>(mut element: T) -> T {
    let opacity = attenuated_opacity(element.style(), 0.72);
    element.active(move |style| style.opacity(opacity))
}

fn attenuated_opacity(base: &StyleRefinement, factor: f32) -> f32 {
    base.opacity.unwrap_or(1.) * factor
}

#[cfg(test)]
mod tests {
    use super::*;
    use gpui::{div, point};

    #[test]
    fn focus_ring_preserves_shadows_and_does_not_override_layout_or_variant_colors() {
        let shadow = BoxShadow::new(px(3.), px(4.), rgba(0x11223340).into()).blur_radius(px(5.));
        let base = StyleRefinement::default()
            .border(px(6.))
            .border_color(rgba(0xAA0000FF))
            .bg(rgba(0xBBCCDDFF))
            .opacity(0.3)
            .shadow(vec![shadow.clone()]);
        let focus = focus_ring_style(&base, 0x445566FF);
        assert_eq!(
            focus.border_widths,
            StyleRefinement::default().border_widths
        );
        assert_eq!(focus.size, StyleRefinement::default().size);
        assert_eq!(focus.padding, StyleRefinement::default().padding);
        assert!(focus.border_color.is_none());
        assert!(focus.background.is_none());
        assert!(focus.opacity.is_none());
        let shadows = focus.box_shadow.unwrap();
        assert_eq!(shadows.len(), 2);
        assert_eq!(shadows[0], shadow);
        assert_eq!(shadows[1].color, rgba(0x445566FF).into());
        assert_eq!(shadows[1].offset, point(px(0.), px(0.)));
        assert_eq!(shadows[1].blur_radius, px(0.));
        assert_eq!(shadows[1].spread_radius, px(2.));
    }

    #[test]
    fn disabled_opacity_multiplies_authored_opacity_without_resurrecting_hidden_controls() {
        for opacity in [0., 0.2, 0.8, 1.] {
            for is_disabled in [false, true] {
                let mut element = disabled(div().opacity(opacity), is_disabled);
                assert_eq!(
                    element.style().opacity,
                    Some(opacity * if is_disabled { 0.5 } else { 1. })
                );
            }
        }
        assert_eq!(disabled(div(), true).style().opacity, Some(0.5));
        assert!(disabled(div(), false).style().opacity.is_none());
    }

    #[test]
    fn pressed_feedback_never_makes_a_translucent_or_hidden_control_more_opaque() {
        for opacity in [0., 0.2, 0.8, 1.] {
            let style = StyleRefinement::default().opacity(opacity);
            assert_eq!(attenuated_opacity(&style, 0.72), opacity * 0.72);
            assert!(attenuated_opacity(&style, 0.72) <= opacity);
        }
        assert_eq!(attenuated_opacity(&StyleRefinement::default(), 0.72), 0.72);
    }
}
