use super::{callbacks, command, configuration};
use crate::abi::{NodeRecord, OpRecord, RenderArena};
use crate::collections::{
    CollectionEngine, ListConfiguration, ListOrientation, engine::CachedBatch, list_configuration,
};
use crate::semantic::{
    COMMAND_LIST_SCROLL_TO_ITEM, COMMAND_LIST_SPLICE, COMPONENT_LIST,
    OP_LIST_ESTIMATED_ITEM_EXTENT_PX, OP_LIST_ITEM_COUNT, OP_LIST_ORIENTATION, OP_LIST_RENDERER,
    ValueKind,
};
use crate::snapshot::{RetainedStrings, SnapshotScratch, ValidatedSnapshot};
use gpui::{ListAlignment, Pixels, px, size};

fn horizontal_configuration() -> ListConfiguration {
    let mut config = configuration(Some(1));
    config.orientation = ListOrientation::Horizontal;
    config.estimated_item_extent = px(160.);
    config
}

fn horizontal_engine() -> CollectionEngine {
    CollectionEngine::new(1, callbacks(), &horizontal_configuration(), 1)
}

fn set_viewport(engine: &mut CollectionEngine, width: f32, height: f32) {
    engine.horizontal.viewport = size(px(width), px(height));
}

#[test]
fn horizontal_widths_start_at_the_estimate() {
    let engine = horizontal_engine();

    assert_eq!(engine.horizontal.total_width(), px(16_000.));
    assert_eq!(engine.horizontal.effective_scroll(), px(0.));
}

#[test]
fn horizontal_max_offset_subtracts_the_viewport() {
    let mut engine = horizontal_engine();
    set_viewport(&mut engine, 800., 200.);

    assert_eq!(engine.horizontal.max_offset(), px(15_200.));
}

#[test]
fn horizontal_visible_range_covers_viewport_plus_overdraw() {
    let mut engine = horizontal_engine();
    set_viewport(&mut engine, 800., 200.);

    let range = engine.horizontal.visible_range(px(240.));
    assert_eq!(range, 0..7);
}

#[test]
fn horizontal_scroll_clamps_to_content() {
    let mut engine = horizontal_engine();
    set_viewport(&mut engine, 800., 200.);

    engine.horizontal.set_scroll(px(100_000.));
    assert_eq!(engine.horizontal.effective_scroll(), px(15_200.));
    engine.horizontal.set_scroll(px(-20.));
    assert_eq!(engine.horizontal.effective_scroll(), px(0.));
}

#[test]
fn horizontal_reveal_moves_the_minimum_distance() {
    let mut engine = horizontal_engine();
    set_viewport(&mut engine, 800., 200.);

    // Item 50 spans 8000..8160; the 800-wide viewport must end at 8160.
    engine.horizontal.reveal(50);
    assert_eq!(engine.horizontal.effective_scroll(), px(7_360.));

    // An already-visible item keeps the viewport still.
    engine.horizontal.reveal(50);
    assert_eq!(engine.horizontal.effective_scroll(), px(7_360.));

    // An item to the left aligns its left edge.
    engine.horizontal.reveal(10);
    assert_eq!(engine.horizontal.effective_scroll(), px(1_600.));
}

#[test]
fn horizontal_page_moves_by_the_viewport_width() {
    let mut engine = horizontal_engine();
    set_viewport(&mut engine, 800., 200.);

    assert_eq!(engine.horizontal.page(true), Some(5));
    assert_eq!(engine.horizontal.effective_scroll(), px(800.));
    assert_eq!(engine.horizontal.page(false), Some(0));
    assert_eq!(engine.horizontal.effective_scroll(), px(0.));
}

#[test]
fn horizontal_measured_widths_shift_the_range() {
    let mut engine = horizontal_engine();
    set_viewport(&mut engine, 800., 200.);
    for index in 0..10 {
        engine.horizontal.record_measurement(index, px(200.));
    }

    // Ten 200-wide items fill 2000px; the viewport plus overdraw reaches 1040px.
    let range = engine.horizontal.visible_range(px(240.));
    assert_eq!(range, 0..6);
    assert_eq!(engine.horizontal.total_width(), px(16_400.));
}

#[test]
fn horizontal_splice_updates_widths_through_committed_hints() {
    let mut engine = horizontal_engine();
    // removed 5 at 50, inserted 8: b packs removed in the high word, inserted below.
    engine.apply_command(&command(COMMAND_LIST_SPLICE, 50, (5_u64 << 32) | 8, ""));
    engine.commit_pending_commands(103);

    assert_eq!(engine.item_count, 103);
    assert_eq!(engine.horizontal.total_width(), px(16_480.));
}

#[test]
fn horizontal_scroll_to_item_reveals_without_moving_the_cursor() {
    let mut engine = horizontal_engine();
    set_viewport(&mut engine, 800., 200.);

    engine.apply_command(&command(COMMAND_LIST_SCROLL_TO_ITEM, 90, 0, ""));
    assert_eq!(engine.horizontal.effective_scroll(), px(13_760.));
    assert_eq!(engine.cursor.active(), Some(0));
}

#[test]
fn horizontal_bottom_alignment_pins_to_the_end() {
    let mut config = horizontal_configuration();
    config.alignment = ListAlignment::Bottom;
    let mut engine = CollectionEngine::new(1, callbacks(), &config, 1);
    set_viewport(&mut engine, 800., 200.);

    assert_eq!(engine.horizontal.effective_scroll(), px(15_200.));

    // Explicit scrolling unpins; switching to Top preserves the offset instead of jumping.
    engine.horizontal.set_scroll(px(100.));
    assert_eq!(engine.horizontal.effective_scroll(), px(100.));
    config.alignment = ListAlignment::Top;
    engine.configure(&config, 2);
    assert_eq!(engine.horizontal.effective_scroll(), px(100.));
}

#[test]
fn horizontal_orientation_switch_rebuilds_widths() {
    let mut engine = CollectionEngine::new(1, callbacks(), &configuration(Some(1)), 1);
    engine.horizontal.viewport = size(px(800.), px(200.));
    engine.horizontal.record_measurement(0, px(10.));
    engine.batches.insert(0, CachedBatch::new());

    let config = horizontal_configuration();
    engine.configure(&config, 2);

    assert_eq!(engine.horizontal.total_width(), px(16_000.));
    assert!(engine.batches.is_empty());
}

#[test]
fn horizontal_configuration_parses_orientation_and_width_ops() {
    static KEY: &[u8] = b"rows";
    let mut nodes = [NodeRecord {
        component: COMPONENT_LIST,
        data_length: KEY.len() as u32,
        ..Default::default()
    }];
    let mut ops = [
        OpRecord {
            code: OP_LIST_ITEM_COUNT,
            value_kind: ValueKind::U32 as u16,
            a: 10,
            ..Default::default()
        },
        OpRecord {
            code: OP_LIST_RENDERER,
            value_kind: ValueKind::Callback as u16,
            a: 7,
            ..Default::default()
        },
        OpRecord {
            code: OP_LIST_ORIENTATION,
            value_kind: ValueKind::U32 as u16,
            a: 1,
            ..Default::default()
        },
        OpRecord {
            code: OP_LIST_ESTIMATED_ITEM_EXTENT_PX,
            value_kind: ValueKind::F32 as u16,
            a: 200f32.to_bits() as u64,
            ..Default::default()
        },
    ];
    let arena = RenderArena {
        nodes: nodes.as_mut_ptr(),
        node_length: 1,
        node_capacity: 1,
        ops: ops.as_mut_ptr(),
        op_length: ops.len() as i32,
        op_capacity: ops.len() as i32,
        children: std::ptr::null_mut(),
        child_length: 0,
        child_capacity: 0,
        utf8: KEY.as_ptr().cast_mut(),
        utf8_length: KEY.len() as i32,
        utf8_capacity: KEY.len() as i32,
        generation: 1,
        flags: 0,
        required_node_capacity: 0,
        required_op_capacity: 0,
        required_child_capacity: 0,
        required_utf8_capacity: 0,
    };
    let mut snapshot = ValidatedSnapshot::default();
    let mut strings = RetainedStrings::default();
    let mut scratch = SnapshotScratch::default();
    snapshot
        .decode_into(&arena, 0, &mut strings, &mut scratch)
        .unwrap();

    let config = list_configuration(&snapshot, &snapshot.nodes[0]).unwrap();
    assert_eq!(config.orientation, ListOrientation::Horizontal);
    assert_eq!(config.estimated_item_extent, px(200.));
    assert_eq!(config.item_count, 10);
    assert_eq!(config.renderer_token, 7);
}

fn decode_extent(orientation: Option<u32>) -> Pixels {
    static KEY: &[u8] = b"rows";
    let mut nodes = [NodeRecord {
        component: COMPONENT_LIST,
        data_length: KEY.len() as u32,
        ..Default::default()
    }];
    let mut ops = [
        OpRecord {
            code: OP_LIST_ITEM_COUNT,
            value_kind: ValueKind::U32 as u16,
            a: 10,
            ..Default::default()
        },
        OpRecord {
            code: OP_LIST_RENDERER,
            value_kind: ValueKind::Callback as u16,
            a: 7,
            ..Default::default()
        },
        OpRecord {
            code: OP_LIST_ORIENTATION,
            value_kind: ValueKind::U32 as u16,
            a: orientation.unwrap_or(0) as u64,
            ..Default::default()
        },
    ];
    // An omitted orientation op is itself the vertical default; only include it when set.
    let op_count = if orientation.is_some() { 3 } else { 2 };
    let arena = RenderArena {
        nodes: nodes.as_mut_ptr(),
        node_length: 1,
        node_capacity: 1,
        ops: ops.as_mut_ptr(),
        op_length: op_count,
        op_capacity: ops.len() as i32,
        children: std::ptr::null_mut(),
        child_length: 0,
        child_capacity: 0,
        utf8: KEY.as_ptr().cast_mut(),
        utf8_length: KEY.len() as i32,
        utf8_capacity: KEY.len() as i32,
        generation: 1,
        flags: 0,
        required_node_capacity: 0,
        required_op_capacity: 0,
        required_child_capacity: 0,
        required_utf8_capacity: 0,
    };
    let mut snapshot = ValidatedSnapshot::default();
    let mut strings = RetainedStrings::default();
    let mut scratch = SnapshotScratch::default();
    snapshot
        .decode_into(&arena, 0, &mut strings, &mut scratch)
        .unwrap();
    let config = list_configuration(&snapshot, &snapshot.nodes[0]).unwrap();
    assert_eq!(
        config.orientation,
        if orientation == Some(1) {
            ListOrientation::Horizontal
        } else {
            ListOrientation::Vertical
        }
    );
    config.estimated_item_extent
}

#[test]
fn omitted_extent_falls_back_to_per_orientation_defaults() {
    assert_eq!(decode_extent(None), px(40.));
    assert_eq!(decode_extent(Some(0)), px(40.));
    assert_eq!(decode_extent(Some(1)), px(160.));
}
