use super::*;
use crate::{native_workloads::WorkloadArena, semantic::*};
use std::cell::RefCell;

thread_local! {
    static CLICKS: RefCell<Vec<(u64, u64)>> = const { RefCell::new(Vec::new()) };
}

unsafe extern "C" fn capture(_: u64, token: u64, payload: u64, _: *const NativeClickEvent) -> i32 {
    CLICKS.with(|clicks| clicks.borrow_mut().push((token, payload)));
    0
}

struct ControlView {
    native: Entity<ManagedView>,
    snapshot: ValidatedSnapshot,
    detached: bool,
    index: usize,
}

impl gpui::Render for ControlView {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        self.native.update(cx, |native, cx| {
            let child = if self.detached {
                materialize_snapshot_node_detached(
                    0,
                    &self.snapshot,
                    native.view_id,
                    native.callbacks,
                    &native.resources,
                    &key(),
                    self.index,
                    Some(73),
                )
            } else {
                native.materialize_node(0, &self.snapshot, window, cx)
            };
            div().size_full().tab_group().child(child)
        })
    }
}

fn snapshot(
    component: u16,
    checked: bool,
    disabled: bool,
    opacity: f32,
    payload: u64,
) -> ValidatedSnapshot {
    let mut arena = WorkloadArena::default();
    let root = arena.node_with_data(component, None, "control");
    arena.op(root, OP_WIDTH_PX, 200f32.to_bits() as u64);
    arena.op(root, OP_HEIGHT_PX, 80f32.to_bits() as u64);
    arena.op(root, OP_OPACITY, opacity.to_bits() as u64);
    arena.op(root, OP_DISABLED, u64::from(disabled));
    if component != COMPONENT_BUTTON {
        arena.op(root, OP_CHECKED, u64::from(checked));
    }
    arena.op(root, OP_BACKGROUND_RGBA, 0x2468ACFF);
    arena.op(root, OP_HOVER_BACKGROUND_RGBA, 0x3579BDFF);
    arena.op(root, OP_ACTIVE_BACKGROUND_RGBA, 0x468ACEFF);
    arena.callback(root, OP_ON_CLICK, 17, payload);
    arena.node_with_data(COMPONENT_TEXT, Some(root), "Control label");
    arena.decode()
}

fn redraw(cx: &mut gpui::VisualTestContext) {
    cx.update(|window, cx| {
        window.refresh();
        window.draw(cx).clear(cx);
    });
}

fn assert_paint(cx: &mut gpui::VisualTestContext, color: u32, opacity: f32) {
    redraw(cx);
    let expected: Hsla = rgba(color).into();
    cx.update(|window, _| {
        let quads = window.painted_quads();
        let painted = quads
            .iter()
            .filter_map(|quad| quad.background.as_solid())
            .find(|color| color.h == expected.h && color.s == expected.s && color.l == expected.l);
        if opacity == 0. {
            assert!(painted.is_none_or(|color| color.a == 0.));
        } else {
            let painted =
                painted.unwrap_or_else(|| panic!("expected {expected:?}; painted {quads:?}"));
            assert!(
                (painted.a - opacity).abs() < 0.0001,
                "{painted:?} != alpha {opacity}"
            );
        }
    });
}

fn press_enter(cx: &mut gpui::VisualTestContext) {
    let keystroke = gpui::Keystroke::parse("enter").unwrap();
    cx.simulate_event(KeyDownEvent {
        keystroke: keystroke.clone(),
        is_held: false,
        prefer_character_input: false,
    });
    cx.simulate_event(KeyUpEvent { keystroke });
}

#[gpui::test]
fn shared_control_presentation_preserves_accessible_names_and_descriptions(
    cx: &mut gpui::TestAppContext,
) {
    use gpui::RenderOnce;
    fn metadata(element: impl gpui::Element) -> gpui::accesskit::Node {
        let mut info = gpui::accesskit::Node::new(element.a11y_role().unwrap());
        element.write_a11y_info(&mut info);
        info
    }
    let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
    for component in [COMPONENT_BUTTON, COMPONENT_CHECKBOX, COMPONENT_RADIO] {
        for explicit in [false, true] {
            let mut arena = WorkloadArena::default();
            let root = arena.node_with_data(component, None, "control");
            arena.node_with_data(COMPONENT_TEXT, Some(root), "Control label");
            if explicit {
                arena.data_op(root, OP_ACCESSIBLE_NAME, "Explicit label");
            }
            arena.data_op(root, OP_ACCESSIBLE_DESCRIPTION, "Supporting description");
            let snapshot = arena.decode();
            cx.draw(
                point(px(0.), px(0.)),
                gpui::size(px(200.), px(100.)),
                |_, _| {
                    canvas(
                        move |_, window, cx| {
                            let presentation = ControlPresentation::new(
                                &snapshot.nodes[0],
                                &snapshot,
                                NativeTheme::default(),
                            );
                            let focus = cx.focus_handle();
                            let info = match component {
                                COMPONENT_BUTTON => metadata(
                                    presentation
                                        .button("control".into(), [])
                                        .track_focus(&focus)
                                        .render(window, cx)
                                        .into_element(),
                                ),
                                COMPONENT_CHECKBOX => metadata(
                                    presentation
                                        .checkbox("control".into(), [])
                                        .track_focus(&focus)
                                        .render(window, cx)
                                        .into_element(),
                                ),
                                COMPONENT_RADIO => metadata(
                                    presentation
                                        .radio("control".into(), [])
                                        .track_focus(&focus)
                                        .render(window, cx)
                                        .into_element(),
                                ),
                                _ => unreachable!(),
                            };
                            assert_eq!(
                                info.label(),
                                Some(if explicit {
                                    "Explicit label"
                                } else {
                                    "Control label"
                                })
                            );
                            assert_eq!(info.description(), Some("Supporting description"));
                            assert_eq!(
                                info.role(),
                                match component {
                                    COMPONENT_BUTTON => gpui::Role::Button,
                                    COMPONENT_CHECKBOX => gpui::Role::CheckBox,
                                    COMPONENT_RADIO => gpui::Role::RadioButton,
                                    _ => unreachable!(),
                                }
                            );
                        },
                        |_, _, _, _| {},
                    )
                    .size_full()
                },
            );
        }
    }
}

#[gpui::test]
fn mounted_and_row_controls_share_disabled_and_interaction_paint(cx: &mut gpui::TestAppContext) {
    cx.update(gpui_base::init);
    for component in [COMPONENT_BUTTON, COMPONENT_CHECKBOX, COMPONENT_RADIO] {
        for detached in [false, true] {
            let native = cx.new(|_| {
                ManagedView::new(1, inert_callbacks(), Default::default(), Default::default())
            });
            let (view, cx) = cx.add_window_view(|_, _| ControlView {
                native,
                snapshot: snapshot(component, true, false, 0.8, 0),
                detached,
                index: 2,
            });
            cx.simulate_resize(gpui::size(px(320.), px(160.)));
            let inside = point(px(100.), px(40.));
            let outside = point(px(300.), px(140.));
            // Replacement must both apply and remove disabled presentation without retaining it.
            for disabled in [false, true, false] {
                view.update(cx, |view, _| {
                    view.snapshot = snapshot(component, true, disabled, 0.8, 0)
                });
                redraw(cx);
                cx.simulate_mouse_move(outside, None, gpui::Modifiers::none());
                assert_paint(cx, 0x2468ACFF, if disabled { 0.4 } else { 0.8 });
                cx.simulate_mouse_move(inside, None, gpui::Modifiers::none());
                assert_paint(
                    cx,
                    if disabled { 0x2468ACFF } else { 0x3579BDFF },
                    if disabled { 0.4 } else { 0.8 },
                );
                cx.simulate_mouse_down(inside, MouseButton::Left, gpui::Modifiers::none());
                assert_paint(
                    cx,
                    if disabled { 0x2468ACFF } else { 0x468ACEFF },
                    if disabled { 0.4 } else { 0.8 },
                );
                cx.simulate_mouse_up(outside, MouseButton::Left, gpui::Modifiers::none());
            }
            view.update(cx, |view, _| {
                view.snapshot = snapshot(component, true, true, 0., 0)
            });
            redraw(cx);
            assert_paint(cx, 0x2468ACFF, 0.);
        }
    }
}

#[gpui::test]
fn shared_presentation_preserves_control_identity_and_click_routing(cx: &mut gpui::TestAppContext) {
    cx.update(gpui_base::init);
    for component in [COMPONENT_BUTTON, COMPONENT_CHECKBOX, COMPONENT_RADIO] {
        for detached in [false, true] {
            for (checked, payload) in [(false, 0), (false, 29), (true, 0)] {
                CLICKS.with(|clicks| clicks.borrow_mut().clear());
                let native = cx.new(|_| {
                    let mut callbacks = inert_callbacks();
                    callbacks.click = Some(capture);
                    ManagedView::new(1, callbacks, Default::default(), Default::default())
                });
                let (view, cx) = cx.add_window_view(|_, _| ControlView {
                    native,
                    snapshot: snapshot(component, checked, false, 1., payload),
                    detached,
                    index: 2,
                });
                redraw(cx);
                cx.simulate_click(point(px(100.), px(40.)), gpui::Modifiers::none());
                let focus =
                    cx.update(|window, cx| window.focused(cx).expect("control takes focus"));
                view.update(cx, |view, _| view.index = 9);
                redraw(cx);
                cx.update(|window, _| {
                    assert!(
                        focus.is_focused(window),
                        "stable item lost focus after reorder"
                    )
                });
                press_enter(cx);
                let expected_payload = if detached && payload == 0 {
                    73
                } else {
                    payload
                };
                let expected_count = if component == COMPONENT_RADIO && checked {
                    0
                } else {
                    2
                };
                CLICKS.with(|clicks| {
                    assert_eq!(
                        *clicks.borrow(),
                        vec![(17, expected_payload); expected_count],
                        "component {component}, detached {detached}"
                    )
                });
                view.update(cx, |view, _| {
                    view.snapshot = snapshot(component, checked, true, 1., payload)
                });
                redraw(cx);
                cx.simulate_click(point(px(100.), px(40.)), gpui::Modifiers::none());
                press_enter(cx);
                CLICKS.with(|clicks| {
                    assert_eq!(
                        clicks.borrow().len(),
                        expected_count,
                        "disabled control activated"
                    )
                });
            }
        }
    }
}
