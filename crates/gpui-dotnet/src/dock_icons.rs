//! Small vector icons for the local Dock skin.
//!
//! The default host ships no icon font and no bundled asset provider, so Dock
//! chrome (collapse chevrons, close, zoom) is drawn as stroked paths through
//! GPUI's canvas instead of text glyphs. Icons inherit their color from the
//! projected foundation theme at the call site and scale from the canvas
//! bounds, so one definition serves every button size.

use gpui::{
    AnyElement, Hsla, IntoElement, Path, PathBuilder, Pixels, Point, Styled as _, canvas, point, px,
};

/// The chrome glyphs the Dock skin needs, each drawn in a 14-unit box.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum DockIcon {
    ChevronLeft,
    ChevronRight,
    ChevronUp,
    ChevronDown,
    Close,
    ZoomIn,
    ZoomOut,
}

/// Draws `kind` in `color` at a square of `size` pixels.
pub(crate) fn icon(kind: DockIcon, color: Hsla, size: Pixels) -> AnyElement {
    let polylines: &[&[(f32, f32)]] = match kind {
        DockIcon::ChevronLeft => &[&[(9.5, 3.5), (4.75, 7.0), (9.5, 10.5)]],
        DockIcon::ChevronRight => &[&[(4.5, 3.5), (9.25, 7.0), (4.5, 10.5)]],
        DockIcon::ChevronUp => &[&[(3.5, 9.0), (7.0, 4.75), (10.5, 9.0)]],
        DockIcon::ChevronDown => &[&[(3.5, 5.0), (7.0, 9.25), (10.5, 5.0)]],
        DockIcon::Close => &[&[(4.0, 4.0), (10.0, 10.0)], &[(10.0, 4.0), (4.0, 10.0)]],
        DockIcon::ZoomIn => &[
            &[(6.0, 2.5), (2.5, 2.5), (2.5, 6.0)],
            &[(8.0, 2.5), (11.5, 2.5), (11.5, 6.0)],
            &[(2.5, 8.0), (2.5, 11.5), (6.0, 11.5)],
            &[(11.5, 8.0), (11.5, 11.5), (8.0, 11.5)],
        ],
        DockIcon::ZoomOut => &[
            &[(2.5, 2.5), (8.0, 2.5), (8.0, 8.0), (2.5, 8.0), (2.5, 2.5)],
            &[
                (6.0, 6.0),
                (11.5, 6.0),
                (11.5, 11.5),
                (6.0, 11.5),
                (6.0, 6.0),
            ],
        ],
    };
    // The paint closure must own what it draws: nothing here may borrow the
    // render frame, which GPUI clears before the next paint.
    let strokes = polylines
        .iter()
        .map(|polyline| polyline.to_vec())
        .collect::<Vec<_>>();
    canvas(
        move |bounds, _, _| {
            let scale = f32::from(bounds.size.width) / 14.0;
            let origin = bounds.origin;
            strokes
                .iter()
                .filter_map(|polyline| stroke_path(polyline, origin, scale))
                .collect::<Vec<_>>()
        },
        move |_, painted, window, _| {
            for path in painted {
                window.paint_path(path, color);
            }
        },
    )
    .w(size)
    .h(size)
    .flex_shrink_0()
    .into_any_element()
}

fn stroke_path(polyline: &[(f32, f32)], origin: Point<Pixels>, scale: f32) -> Option<Path<Pixels>> {
    let mut points = polyline
        .iter()
        .map(|(x, y)| point(origin.x + px(x * scale), origin.y + px(y * scale)));
    let mut builder = PathBuilder::stroke(px(1.75 * scale.max(0.5)));
    builder.move_to(points.next()?);
    for next in points {
        builder.line_to(next);
    }
    builder.build().ok()
}

#[cfg(test)]
mod tests {
    use super::*;
    use gpui::{ParentElement as _, Render, div};

    struct IconSheet;

    impl Render for IconSheet {
        fn render(
            &mut self,
            _: &mut gpui::Window,
            _: &mut gpui::Context<Self>,
        ) -> impl IntoElement {
            div()
                .flex()
                .flex_row()
                .children(
                    [
                        DockIcon::ChevronLeft,
                        DockIcon::ChevronRight,
                        DockIcon::ChevronUp,
                        DockIcon::ChevronDown,
                        DockIcon::Close,
                        DockIcon::ZoomIn,
                        DockIcon::ZoomOut,
                    ]
                    .into_iter()
                    .map(|kind| icon(kind, Hsla::default(), px(14.))),
                )
                .into_any_element()
        }
    }

    /// Every glyph paints without panic through the canvas path.
    #[gpui::test]
    fn all_dock_icons_draw(cx: &mut gpui::TestAppContext) {
        cx.update(|cx| {
            gpui_base::init(cx);
        });
        let (_, cx) = cx.add_window_view(|_, _| IconSheet);
        cx.update(|window, cx| {
            window.draw(cx).clear(cx);
        });
    }
}
