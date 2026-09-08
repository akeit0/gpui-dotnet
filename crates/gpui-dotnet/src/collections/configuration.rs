use std::{rc::Rc, sync::Arc};

use gpui::{ListAlignment, Pixels, SharedString, px};

use crate::{
    resources::ResourceKey,
    scrolling::{DEFAULT_SCROLLBAR_WIDTH, ScrollbarMetrics},
    semantic::{
        OP_LIST_ALIGNMENT, OP_LIST_BATCH_SIZE, OP_LIST_CONTENT_REVISION,
        OP_LIST_ESTIMATED_ITEM_HEIGHT_PX, OP_LIST_ITEM_COUNT, OP_LIST_ON_ACTIVATED,
        OP_LIST_ON_SELECTION_REQUESTED, OP_LIST_OVERDRAW_PX, OP_LIST_PROJECTION_REVISION,
        OP_LIST_RENDERER, OP_RESOURCE_OWNER, OP_SCROLLBAR_GUTTER, OP_SCROLLBAR_WIDTH,
        OP_TABLE_COLUMN,
    },
    snapshot::{SnapshotNode, ValidatedSnapshot},
};

pub(crate) struct ListConfiguration {
    pub(crate) tooltip_token: u64,
    pub(crate) tooltip: crate::tooltip::TooltipConfiguration,
    pub(crate) item_count: usize,
    pub(crate) renderer_token: u64,
    pub(crate) activation_token: u64,
    pub(crate) selection_token: u64,
    pub(crate) batch_size: usize,
    pub(crate) overdraw: Pixels,
    pub(crate) alignment: ListAlignment,
    pub(crate) estimated_item_height: Pixels,
    pub(crate) content_revision: Option<u64>,
    pub(crate) scrollbar: ScrollbarMetrics,
    pub(crate) projection_revision: Option<u64>,
}

/// One declared table column. Widths are declarative intents (px or fraction); the native
/// header strip and every reconciled row cell use the same resolved intent so Taffy aligns them.
#[derive(Clone, Debug, PartialEq)]
pub(crate) struct TableColumnSpec {
    pub(crate) key: SharedString,
    pub(crate) header: SharedString,
    pub(crate) width: Pixels,
    pub(crate) width_is_fraction: bool,
    pub(crate) alignment: u32,
}

/// Column metadata for one table resource. A change between snapshots means row layout
/// changed, which invalidates every cached row batch.
#[derive(Clone, Debug, PartialEq)]
pub(crate) struct TableSpec {
    pub(crate) columns: Vec<TableColumnSpec>,
}

/// Packs one column's numeric record into an OP_TABLE_COLUMN payload: width f32 bits in the
/// low word, unit in bits 32..34, alignment in bits 34..36. Bits 36+ must be zero. Mirrored by
/// the managed `PackTableColumn`; kept for native tests of the record layout.
#[cfg(test)]
pub(crate) const fn pack_table_column(width_bits: u32, unit: u32, alignment: u32) -> u64 {
    width_bits as u64 | ((unit as u64) << 32) | ((alignment as u64) << 34)
}

pub(crate) fn unpack_table_column(record: u64) -> Option<(f32, bool, u32)> {
    if record >> 36 != 0 {
        return None;
    }
    let unit = ((record >> 32) & 0b11) as u32;
    let alignment = ((record >> 34) & 0b11) as u32;
    if unit > 1 || alignment > 2 {
        return None;
    }
    let width = f32::from_bits((record & 0xFFFF_FFFF) as u32);
    if !width.is_finite() || width <= 0.0 || (unit == 1 && width > 1.0) {
        return None;
    }
    Some((width, unit == 1, alignment))
}

/// Table node data is the row-engine key followed by one NUL-separated key/header string pair
/// per column; the numeric width/unit/alignment records arrive as one OP_TABLE_COLUMN op per
/// column in the same order. Malformed combinations return `None`; the materializer falls back
/// to an error element.
pub(crate) fn parse_table_spec(
    data: &str,
    records: &[u64],
) -> Option<(SharedString, Vec<TableColumnSpec>)> {
    let strings: Vec<&str> = data.split('\0').collect();
    let key = strings.first()?;
    if key.is_empty() || strings.len() - 1 != records.len() * 2 {
        return None;
    }
    let mut columns = Vec::with_capacity(records.len());
    for (index, record) in records.iter().enumerate() {
        let column_key = strings[1 + index * 2];
        let header = strings[2 + index * 2];
        if column_key.is_empty() {
            return None;
        }
        let (width, width_is_fraction, alignment) = unpack_table_column(*record)?;
        columns.push(TableColumnSpec {
            key: shared(column_key),
            header: shared(header),
            width: px(width),
            width_is_fraction,
            alignment,
        });
    }
    Some((shared(key), columns))
}

pub(crate) fn table_key(snapshot: &ValidatedSnapshot, node: &SnapshotNode) -> Option<ResourceKey> {
    let owner = last_u32(snapshot, node, OP_RESOURCE_OWNER)?;
    if owner == 0 || node.data.is_empty() {
        return None;
    }
    let key = node.data.split('\0').next()?;
    if key.is_empty() {
        return None;
    }
    Some(ResourceKey::new(owner, shared(key)))
}

pub(crate) fn table_configuration(
    snapshot: &ValidatedSnapshot,
    node: &SnapshotNode,
) -> Option<(ResourceKey, Rc<TableSpec>)> {
    let owner = last_u32(snapshot, node, OP_RESOURCE_OWNER)?;
    if owner == 0 || node.data.is_empty() {
        return None;
    }
    let records: Vec<u64> = snapshot
        .ops(node)
        .iter()
        .filter(|op| op.code == OP_TABLE_COLUMN)
        .map(|op| op.a)
        .collect();
    let (key, columns) = parse_table_spec(&node.data, &records)?;
    Some((ResourceKey::new(owner, key), Rc::new(TableSpec { columns })))
}

pub(crate) fn list_configuration(
    snapshot: &ValidatedSnapshot,
    node: &SnapshotNode,
) -> Option<ListConfiguration> {
    let item_count = last_u32(snapshot, node, OP_LIST_ITEM_COUNT)? as usize;
    let renderer = snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == OP_LIST_RENDERER)?
        .a;
    let batch_size = last_u32(snapshot, node, OP_LIST_BATCH_SIZE)
        .unwrap_or(48)
        .clamp(1, 512) as usize;
    let overdraw = snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == OP_LIST_OVERDRAW_PX)
        .map_or(px(240.), |op| px(f32::from_bits(op.a as u32)));
    let alignment = match last_u32(snapshot, node, OP_LIST_ALIGNMENT).unwrap_or(0) {
        1 => ListAlignment::Bottom,
        _ => ListAlignment::Top,
    };
    let estimated_item_height = snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == OP_LIST_ESTIMATED_ITEM_HEIGHT_PX)
        .map_or(px(40.), |op| px(f32::from_bits(op.a as u32)));
    let content_revision = snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == OP_LIST_CONTENT_REVISION)
        .map(|op| op.a);
    let scrollbar_gutter = last_u32(snapshot, node, OP_SCROLLBAR_GUTTER).is_some_and(|v| v != 0);
    let scrollbar_width = last_op_bits_f32(snapshot, node, OP_SCROLLBAR_WIDTH)
        .unwrap_or(DEFAULT_SCROLLBAR_WIDTH.into());
    let scrollbar = ScrollbarMetrics::new(px(scrollbar_width), scrollbar_gutter);
    Some(ListConfiguration {
        tooltip_token: snapshot
            .ops(node)
            .iter()
            .rev()
            .find(|op| op.code == crate::semantic::OP_LIST_ON_TOOLTIP_REQUESTED)
            .map_or(0, |op| op.a),
        tooltip: crate::tooltip::TooltipConfiguration::from_snapshot(snapshot, node),
        item_count,
        renderer_token: renderer,
        activation_token: snapshot
            .ops(node)
            .iter()
            .rev()
            .find(|op| op.code == OP_LIST_ON_ACTIVATED)
            .map_or(0, |op| op.a),
        selection_token: snapshot
            .ops(node)
            .iter()
            .rev()
            .find(|op| op.code == OP_LIST_ON_SELECTION_REQUESTED)
            .map_or(0, |op| op.a),
        batch_size,
        overdraw,
        alignment,
        estimated_item_height,
        content_revision,
        scrollbar,
        projection_revision: snapshot
            .ops(node)
            .iter()
            .rev()
            .find(|op| op.code == OP_LIST_PROJECTION_REVISION)
            .map(|op| op.a),
    })
}

/// Reads an f32-valued op as bits. Returns `None` when the op is absent.
pub(crate) fn last_op_bits_f32(
    snapshot: &ValidatedSnapshot,
    node: &SnapshotNode,
    code: u16,
) -> Option<f32> {
    snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == code)
        .map(|op| f32::from_bits(op.a as u32))
}

pub(crate) fn last_u32(
    snapshot: &ValidatedSnapshot,
    node: &SnapshotNode,
    code: u16,
) -> Option<u32> {
    snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == code)
        .map(|op| op.a as u32)
}

pub(crate) fn last_callback(snapshot: &ValidatedSnapshot, node: &SnapshotNode, code: u16) -> u64 {
    snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == code)
        .map_or(0, |op| op.a)
}

pub(crate) fn shared(value: &str) -> SharedString {
    SharedString::new(Arc::<str>::from(value))
}
