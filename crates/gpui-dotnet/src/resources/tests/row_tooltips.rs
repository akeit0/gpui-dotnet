use super::*;
use gpui::{MouseButton, Styled};
use std::time::Duration;

struct TooltipRows {
    store: Rc<ResourceStore>,
    resource: Rc<RefCell<ManagedListResource>>,
    paints: Rc<Cell<usize>>,
    show_rows: bool,
    show_tooltip: bool,
    horizontal: bool,
}

impl gpui::Render for TooltipRows {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        self.paints.set(0);
        self.store.row_tooltips.begin_frame();
        self.resource.borrow_mut().begin_frame();
        let mut root = div()
            .flex()
            .flex_col()
            .w(px(if self.horizontal { 600. } else { 300. }))
            .h(px(240.));
        if self.show_rows && self.horizontal {
            // Exercise the same cached item targets side by side, independently of List's
            // vertical layout adapter. Anchoring must use target bounds on both axes.
            let mut items = div().flex().w(px(400.)).h(px(40.));
            for index in 0..2 {
                items = items.child(self.resource.borrow_mut().render_item(
                    index,
                    &self.store,
                    &ResourceKey::new(1, "tooltip-rows".into()),
                ));
            }
            root = root.child(items);
        } else if self.show_rows {
            let state = self.resource.borrow().state.clone();
            let resource = self.resource.clone();
            let store = self.store.clone();
            root = root.child(
                gpui::list(state, move |index, _, _| {
                    resource.borrow_mut().render_item(
                        index,
                        &store,
                        &ResourceKey::new(1, "tooltip-rows".into()),
                    )
                })
                .size_full(),
            );
        }
        let request = ARTIFACTS.with(|capture| {
            capture
                .borrow()
                .activations
                .last()
                .map(|event| u64::from_le_bytes(event.3[16..24].try_into().unwrap()))
        });
        if self.show_tooltip
            && let Some(id) = request
        {
            let paints = self.paints.clone();
            let content = div().w(px(150.)).h(px(80.)).child(gpui::canvas(
                |_, _, _| (),
                move |_, _, _, _| paints.set(paints.get() + 1),
            ));
            root = root.child(crate::row_tooltip::tooltip(
                self.store.row_tooltips.clone(),
                id,
                1,
                content.into_any_element(),
                window,
                cx,
            ));
        }
        self.store.row_tooltips.finish_declarations(window);
        root.child(crate::row_tooltip::frame_end(
            self.store.row_tooltips.clone(),
        ))
    }
}

fn fixture(
    cx: &mut gpui::TestAppContext,
) -> (
    Entity<TooltipRows>,
    &mut gpui::VisualTestContext,
    Rc<RefCell<ManagedListResource>>,
    Rc<Cell<usize>>,
) {
    cx.update(gpui_base::init);
    ARTIFACTS.with(|capture| {
        *capture.borrow_mut() = ArtifactCapture {
            item_ids: true,
            tooltip_targets: true,
            ..Default::default()
        }
    });
    let callbacks = ManagedCallbacks {
        control_event: Some(capture_row_menu),
        ..artifact_callbacks()
    };
    let store = Rc::new(ResourceStore::new(1, callbacks, theme()));
    let mut config = configuration(Some(7));
    config.tooltip_token = (1u64 << 32) | 44;
    config.tooltip.placement = 3;
    config.tooltip.alignment = 0;
    config.item_count = 20;
    let resource = store.list_resource(&ResourceKey::new(1, "tooltip-rows".into()), &config, 1);
    let paints = Rc::new(Cell::new(0));
    let (view, cx) = cx.add_window_view(|_, _| TooltipRows {
        store,
        resource: resource.clone(),
        paints: paints.clone(),
        show_rows: true,
        show_tooltip: true,
        horizontal: false,
    });
    draw(cx);
    (view, cx, resource, paints)
}

fn draw(cx: &mut gpui::VisualTestContext) {
    cx.update(|window, cx| {
        window.refresh();
        window.draw(cx).clear(cx);
    });
}
fn advance(cx: &mut gpui::VisualTestContext, milliseconds: u64) {
    cx.executor()
        .advance_clock(Duration::from_millis(milliseconds));
    cx.run_until_parked();
}
fn move_to(cx: &mut gpui::VisualTestContext, x: f32, y: f32) {
    cx.simulate_mouse_move(point(px(x), px(y)), None, gpui::Modifiers::none());
}
fn requests() -> usize {
    ARTIFACTS.with(|capture| capture.borrow().activations.len())
}

#[gpui::test]
fn row_tooltip_uses_collection_timing_and_expires_when_options_change(
    cx: &mut gpui::TestAppContext,
) {
    let (_, cx, resource, paints) = fixture(cx);
    resource.borrow_mut().tooltip.show_delay_ms = 120;
    resource.borrow_mut().tooltip.hide_delay_ms = 650;
    move_to(cx, 50., 60.);
    advance(cx, 119);
    assert_eq!(requests(), 0);
    advance(cx, 1);
    draw(cx);
    assert_eq!(paints.get(), 1);
    move_to(cx, 250., 220.);
    advance(cx, 649);
    draw(cx);
    assert_eq!(paints.get(), 1);
    advance(cx, 1);
    draw(cx);
    assert_eq!(paints.get(), 0);

    move_to(cx, 50., 60.);
    advance(cx, 120);
    draw(cx);
    assert_eq!(paints.get(), 1);
    resource.borrow_mut().tooltip.hide_delay_ms = 0;
    draw(cx);
    assert_eq!(
        paints.get(),
        0,
        "changed options kept an obsolete hover request"
    );
    move_to(cx, 250., 220.);
    move_to(cx, 50., 60.);
    advance(cx, 120);
    draw(cx);
    assert_eq!(paints.get(), 1);
    move_to(cx, 250., 220.);
    advance(cx, 0);
    draw(cx);
    assert_eq!(
        paints.get(),
        0,
        "zero hide delay did not dismiss immediately"
    );
}

#[gpui::test]
fn row_tooltip_anchors_to_horizontal_item_bounds(cx: &mut gpui::TestAppContext) {
    let (view, cx, resource, paints) = fixture(cx);
    resource.borrow_mut().tooltip.placement = 2;
    view.update(cx, |view, _| view.horizontal = true);
    draw(cx);
    move_to(cx, 250., 20.);
    advance(cx, 500);
    draw(cx);
    assert_eq!(requests(), 1);
    assert_eq!(paints.get(), 1);
    ARTIFACTS.with(|capture| {
        let capture = capture.borrow();
        assert_eq!(
            u64::from_le_bytes(capture.activations[0].3[8..16].try_into().unwrap()),
            1001
        );
    });
    move_to(cx, 450., 20.); // Right of the second item's actual bounds.
    advance(cx, 350);
    draw(cx);
    assert_eq!(paints.get(), 1, "right-placed content was not reachable");
    move_to(cx, 580., 200.);
    advance(cx, 300);
    draw(cx);
    assert_eq!(paints.get(), 0);
}

#[gpui::test]
fn row_tooltip_waits_for_hover_and_allows_entering_its_content(cx: &mut gpui::TestAppContext) {
    let (_, cx, resource, paints) = fixture(cx);
    move_to(cx, 50., 60.);
    advance(cx, 499);
    assert_eq!(requests(), 0);
    move_to(cx, 250., 60.);
    advance(cx, 10);
    assert_eq!(requests(), 0, "brief hover requested managed content");
    move_to(cx, 50., 60.);
    // A root render replaces callback tokens; the timer must use the live binding.
    resource.borrow_mut().tooltip_token = (1u64 << 32) | 45;
    draw(cx);
    advance(cx, 500);
    draw(cx);
    assert_eq!(requests(), 1);
    assert_eq!(paints.get(), 1);
    ARTIFACTS.with(|capture| {
        let capture = capture.borrow();
        let (token, flags, revision, bytes) = &capture.activations[0];
        assert_eq!((*token, *flags, *revision), ((1u64 << 32) | 45, 2, 7));
        assert_eq!(u64::from_le_bytes(bytes[8..16].try_into().unwrap()), 1001);
    });
    move_to(cx, 50., 100.); // The tooltip starts just below row 1.
    advance(cx, 350);
    draw(cx);
    assert_eq!(paints.get(), 1, "entering content dismissed the tooltip");
    assert_eq!(requests(), 1);
    move_to(cx, 250., 220.);
    advance(cx, 300);
    draw(cx);
    assert_eq!(paints.get(), 0);
    assert_eq!(requests(), 1);
}

#[gpui::test]
fn row_tooltip_expires_on_scroll_press_replacement_and_removal(cx: &mut gpui::TestAppContext) {
    let (view, cx, resource, paints) = fixture(cx);
    for change in 0..6 {
        move_to(cx, 250., 10.);
        move_to(cx, 50., 60.);
        advance(cx, 500);
        draw(cx);
        assert_eq!(paints.get(), 1, "tooltip missing before change {change}");
        let before = requests();
        match change {
            0 => {
                cx.simulate_event(gpui::ScrollWheelEvent {
                    position: point(px(250.), px(60.)),
                    delta: gpui::ScrollDelta::Pixels(point(px(0.), px(-40.))),
                    ..Default::default()
                });
            }
            1 => cx.simulate_mouse_down(
                point(px(250.), px(60.)),
                MouseButton::Right,
                gpui::Modifiers::none(),
            ),
            2 => resource.borrow_mut().clear_batches(),
            3 => view.update(cx, |view, _| view.show_rows = false),
            4 => view.update(cx, |view, _| view.show_tooltip = false),
            _ => resource.borrow_mut().scroll_to_item(19),
        }
        draw(cx);
        assert_eq!(paints.get(), 0, "tooltip survived change {change}");
        view.update(cx, |view, _| {
            view.show_rows = true;
            view.show_tooltip = true;
        });
        resource.borrow_mut().scroll_to_item(0);
        draw(cx);
        advance(cx, 600);
        assert_eq!(
            requests(),
            before,
            "stale request reopened after change {change}"
        );
    }
}
