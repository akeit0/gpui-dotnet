use gpui::{
    AnyElement, App, Bounds, ElementId, InteractiveElement, IntoElement, ParentElement, Pixels,
    Styled, Window, canvas, deferred, div,
};
use gpui_base::{Align, Placement, Positioner};

#[derive(Clone, Copy)]
pub(crate) struct SidePopupOptions {
    pub(crate) placement: Placement,
    pub(crate) align: Align,
    pub(crate) offset: Pixels,
    pub(crate) margin: Pixels,
    pub(crate) priority: usize,
}

#[derive(Default)]
struct SidePopupAnchorState {
    bounds: Bounds<Pixels>,
    captured: bool,
}

pub(crate) fn side_popup(
    id: ElementId,
    trigger: AnyElement,
    content: Option<AnyElement>,
    options: SidePopupOptions,
    window: &mut Window,
    cx: &mut App,
) -> AnyElement {
    let state = window.use_keyed_state((id.clone(), "side-popup-anchor"), cx, |_, _| {
        SidePopupAnchorState::default()
    });
    let root = div()
        .id((id, "side-popup-host"))
        .relative()
        .flex()
        .flex_none()
        .child(trigger)
        .child(
            canvas(
                {
                    let state = state.clone();
                    move |bounds, window, cx| {
                        let first = state.update(cx, |state, _| {
                            let first = !state.captured;
                            state.bounds = bounds;
                            state.captured = true;
                            first
                        });
                        if first {
                            window.request_animation_frame();
                        }
                    }
                },
                |_, _, _, _| {},
            )
            .absolute()
            .size_full()
            .top_0()
            .left_0(),
        );

    let Some(content) = content else {
        return root.into_any_element();
    };
    if !state.read(cx).captured {
        return root.into_any_element();
    }

    let trigger_bounds = state.read(cx).bounds;
    let popup = Positioner::side(trigger_bounds)
        .placement(options.placement)
        .align(options.align)
        .offset(options.offset)
        .margin(options.margin)
        .occlude()
        .child(content);
    root.child(deferred(popup).with_priority(options.priority))
        .into_any_element()
}

#[cfg(test)]
mod tests {
    use super::*;
    use gpui::{Context, Render, px};

    struct SidePopupHarness;

    impl Render for SidePopupHarness {
        fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
            let popup = side_popup(
                "side-popup".into(),
                div()
                    .debug_selector(|| "side-popup-trigger".into())
                    .size(px(100.))
                    .into_any_element(),
                Some(
                    div()
                        .debug_selector(|| "side-popup-content".into())
                        .size(px(20.))
                        .into_any_element(),
                ),
                SidePopupOptions {
                    placement: Placement::Bottom,
                    align: Align::Start,
                    offset: px(8.),
                    margin: px(8.),
                    priority: 100,
                },
                window,
                cx,
            );
            div().flex().items_start().child(popup)
        }
    }

    #[gpui::test]
    fn positions_content_from_the_measured_trigger(cx: &mut gpui::TestAppContext) {
        let (_, window) = cx.add_window_view(|_, _| SidePopupHarness);
        window.update(|window, cx| window.draw(cx).clear(cx));
        window.update(|window, cx| window.draw(cx).clear(cx));

        let trigger = window.debug_bounds("side-popup-trigger").unwrap();
        let content = window.debug_bounds("side-popup-content").unwrap();

        assert_eq!(content.left(), px(8.));
        assert_eq!(content.top(), trigger.bottom() + px(8.));
    }
}
