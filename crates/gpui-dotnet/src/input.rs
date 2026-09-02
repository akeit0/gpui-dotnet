use std::{ops::Range, sync::Arc};

use gpui::{
    App, Bounds, ClipboardItem, Context, CursorStyle, Element, ElementId, ElementInputHandler,
    Entity, EntityInputHandler, FocusHandle, Focusable, GlobalElementId, IntoElement, KeyBinding,
    LayoutId, MouseButton, MouseDownEvent, MouseMoveEvent, MouseUpEvent, PaintQuad, Pixels, Point,
    Render, Role, ShapedLine, SharedString, Style, Subscription, TextAlign, TextRun,
    UTF16Selection, UnderlineStyle, Window, actions, div, fill, point, prelude::*, px, relative,
    rgba, size,
};
use unicode_segmentation::UnicodeSegmentation;

use crate::{
    abi::{ManagedCallbacks, NativeControlEvent},
    resources::ResourceCommand,
    theme::SharedTheme,
};

actions!(
    gpui_dotnet_input,
    [
        Backspace,
        Delete,
        Left,
        Right,
        SelectLeft,
        SelectRight,
        SelectAll,
        Home,
        End,
        Paste,
        Cut,
        Copy,
        Submit,
    ]
);

const EVENT_CHANGED: u16 = 1;
const EVENT_SUBMITTED: u16 = 2;
const EVENT_FOCUS_CHANGED: u16 = 3;

pub(crate) fn init(cx: &mut App) {
    cx.bind_keys([
        KeyBinding::new("backspace", Backspace, Some("GpuiDotnetInput")),
        KeyBinding::new("delete", Delete, Some("GpuiDotnetInput")),
        KeyBinding::new("left", Left, Some("GpuiDotnetInput")),
        KeyBinding::new("right", Right, Some("GpuiDotnetInput")),
        KeyBinding::new("shift-left", SelectLeft, Some("GpuiDotnetInput")),
        KeyBinding::new("shift-right", SelectRight, Some("GpuiDotnetInput")),
        KeyBinding::new("secondary-a", SelectAll, Some("GpuiDotnetInput")),
        KeyBinding::new("home", Home, Some("GpuiDotnetInput")),
        KeyBinding::new("end", End, Some("GpuiDotnetInput")),
        KeyBinding::new("secondary-v", Paste, Some("GpuiDotnetInput")),
        KeyBinding::new("secondary-x", Cut, Some("GpuiDotnetInput")),
        KeyBinding::new("secondary-c", Copy, Some("GpuiDotnetInput")),
        KeyBinding::new("enter", Submit, Some("GpuiDotnetInput")),
    ]);
}

#[derive(Clone, Copy, Default)]
pub(crate) struct InputBindings {
    pub(crate) changed: u64,
    pub(crate) submitted: u64,
    pub(crate) focus_changed: u64,
}

pub(crate) struct InputInitialState<'a> {
    pub(crate) value: &'a str,
    pub(crate) placeholder: &'a str,
    pub(crate) disabled: bool,
    pub(crate) read_only: bool,
    pub(crate) password: bool,
    pub(crate) bindings: InputBindings,
}

pub(crate) struct ManagedInput {
    session_id: u64,
    callbacks: ManagedCallbacks,
    focus_handle: FocusHandle,
    content: SharedString,
    last_emitted_content: SharedString,
    placeholder: SharedString,
    selected_range: Range<usize>,
    selection_reversed: bool,
    marked_range: Option<Range<usize>>,
    last_layout: Option<ShapedLine>,
    last_bounds: Option<Bounds<Pixels>>,
    scroll_x: Pixels,
    is_selecting: bool,
    disabled: bool,
    read_only: bool,
    password: bool,
    bindings: InputBindings,
    revision: u64,
    callback_error: Option<i32>,
    focus_subscriptions: Vec<Subscription>,
    theme: SharedTheme,
}

impl ManagedInput {
    pub(crate) fn new(
        session_id: u64,
        callbacks: ManagedCallbacks,
        initial: InputInitialState<'_>,
        theme: SharedTheme,
        cx: &mut Context<Self>,
    ) -> Self {
        let content = single_line(initial.value);
        let cursor = content.len();
        Self {
            session_id,
            callbacks,
            focus_handle: cx.focus_handle().tab_stop(true),
            last_emitted_content: content.clone(),
            content,
            placeholder: shared(initial.placeholder),
            selected_range: cursor..cursor,
            selection_reversed: false,
            marked_range: None,
            last_layout: None,
            last_bounds: None,
            scroll_x: px(0.),
            is_selecting: false,
            disabled: initial.disabled,
            read_only: initial.read_only,
            password: initial.password,
            bindings: initial.bindings,
            revision: 0,
            callback_error: None,
            focus_subscriptions: Vec::new(),
            theme,
        }
    }

    pub(crate) fn configure(
        &mut self,
        placeholder: &str,
        disabled: bool,
        read_only: bool,
        password: bool,
        bindings: InputBindings,
        cx: &mut Context<Self>,
    ) {
        let placeholder = shared(placeholder);
        let changed = self.placeholder != placeholder
            || self.disabled != disabled
            || self.read_only != read_only
            || self.password != password
            || self.bindings.changed != bindings.changed
            || self.bindings.submitted != bindings.submitted
            || self.bindings.focus_changed != bindings.focus_changed;
        self.placeholder = placeholder;
        self.disabled = disabled;
        self.read_only = read_only;
        self.password = password;
        self.bindings = bindings;
        if disabled {
            self.is_selecting = false;
        }
        if changed {
            cx.notify();
        }
    }

    pub(crate) fn apply_command(
        &mut self,
        command: &ResourceCommand,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) {
        match command.command {
            20 if !self.disabled => self.focus_handle.focus(window, cx),
            21 if self.focus_handle.is_focused(window) => window.blur(cx),
            22 => self.set_value(command.data.as_ref(), cx),
            23 if !self.disabled => {
                self.selected_range = 0..self.content.len();
                self.selection_reversed = false;
                cx.notify();
            }
            _ => {}
        }
    }

    fn set_value(&mut self, value: &str, cx: &mut Context<Self>) {
        let content = single_line(value);
        let cursor = content.len();
        self.content = content.clone();
        self.last_emitted_content = content;
        self.selected_range = cursor..cursor;
        self.selection_reversed = false;
        self.marked_range = None;
        self.scroll_x = px(0.);
        cx.notify();
    }

    fn can_edit(&self) -> bool {
        !self.disabled && !self.read_only
    }

    fn left(&mut self, _: &Left, _: &mut Window, cx: &mut Context<Self>) {
        if self.disabled {
            return;
        }
        if self.selected_range.is_empty() {
            self.move_to(self.previous_boundary(self.cursor_offset()), cx);
        } else {
            self.move_to(self.selected_range.start, cx);
        }
    }

    fn right(&mut self, _: &Right, _: &mut Window, cx: &mut Context<Self>) {
        if self.disabled {
            return;
        }
        if self.selected_range.is_empty() {
            self.move_to(self.next_boundary(self.cursor_offset()), cx);
        } else {
            self.move_to(self.selected_range.end, cx);
        }
    }

    fn select_left(&mut self, _: &SelectLeft, _: &mut Window, cx: &mut Context<Self>) {
        if !self.disabled {
            self.select_to(self.previous_boundary(self.cursor_offset()), cx);
        }
    }

    fn select_right(&mut self, _: &SelectRight, _: &mut Window, cx: &mut Context<Self>) {
        if !self.disabled {
            self.select_to(self.next_boundary(self.cursor_offset()), cx);
        }
    }

    fn select_all(&mut self, _: &SelectAll, _: &mut Window, cx: &mut Context<Self>) {
        if !self.disabled {
            self.selected_range = 0..self.content.len();
            self.selection_reversed = false;
            cx.notify();
        }
    }

    fn home(&mut self, _: &Home, _: &mut Window, cx: &mut Context<Self>) {
        if !self.disabled {
            self.move_to(0, cx);
        }
    }

    fn end(&mut self, _: &End, _: &mut Window, cx: &mut Context<Self>) {
        if !self.disabled {
            self.move_to(self.content.len(), cx);
        }
    }

    fn backspace(&mut self, _: &Backspace, window: &mut Window, cx: &mut Context<Self>) {
        if !self.can_edit() {
            return;
        }
        if self.selected_range.is_empty() {
            self.select_to(self.previous_boundary(self.cursor_offset()), cx);
        }
        self.replace_text_in_range(None, "", window, cx);
    }

    fn delete(&mut self, _: &Delete, window: &mut Window, cx: &mut Context<Self>) {
        if !self.can_edit() {
            return;
        }
        if self.selected_range.is_empty() {
            self.select_to(self.next_boundary(self.cursor_offset()), cx);
        }
        self.replace_text_in_range(None, "", window, cx);
    }

    fn paste(&mut self, _: &Paste, window: &mut Window, cx: &mut Context<Self>) {
        if !self.can_edit() {
            return;
        }
        if let Some(text) = cx.read_from_clipboard().and_then(|item| item.text()) {
            self.replace_text_in_range(None, single_line(&text).as_ref(), window, cx);
        }
    }

    fn copy(&mut self, _: &Copy, _: &mut Window, cx: &mut Context<Self>) {
        if !self.disabled && !self.selected_range.is_empty() {
            cx.write_to_clipboard(ClipboardItem::new_string(
                self.content[self.selected_range.clone()].to_string(),
            ));
        }
    }

    fn cut(&mut self, _: &Cut, window: &mut Window, cx: &mut Context<Self>) {
        if !self.can_edit() || self.selected_range.is_empty() {
            return;
        }
        cx.write_to_clipboard(ClipboardItem::new_string(
            self.content[self.selected_range.clone()].to_string(),
        ));
        self.replace_text_in_range(None, "", window, cx);
    }

    fn submit(&mut self, _: &Submit, _: &mut Window, cx: &mut Context<Self>) {
        if !self.disabled {
            self.emit(self.bindings.submitted, EVENT_SUBMITTED, false, cx);
        }
    }

    fn on_mouse_down(
        &mut self,
        event: &MouseDownEvent,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) {
        if self.disabled {
            return;
        }
        self.focus_handle.focus(window, cx);
        self.is_selecting = true;
        let offset = self.index_for_mouse_position(event.position);
        if event.modifiers.shift {
            self.select_to(offset, cx);
        } else {
            self.move_to(offset, cx);
        }
    }

    fn on_mouse_up(&mut self, _: &MouseUpEvent, _: &mut Window, _: &mut Context<Self>) {
        self.is_selecting = false;
    }

    fn on_mouse_move(&mut self, event: &MouseMoveEvent, _: &mut Window, cx: &mut Context<Self>) {
        if self.is_selecting && !self.disabled {
            self.select_to(self.index_for_mouse_position(event.position), cx);
        }
    }

    fn move_to(&mut self, offset: usize, cx: &mut Context<Self>) {
        self.selected_range = offset..offset;
        self.selection_reversed = false;
        cx.notify();
    }

    fn select_to(&mut self, offset: usize, cx: &mut Context<Self>) {
        if self.selection_reversed {
            self.selected_range.start = offset;
        } else {
            self.selected_range.end = offset;
        }
        if self.selected_range.end < self.selected_range.start {
            self.selection_reversed = !self.selection_reversed;
            self.selected_range = self.selected_range.end..self.selected_range.start;
        }
        cx.notify();
    }

    fn cursor_offset(&self) -> usize {
        if self.selection_reversed {
            self.selected_range.start
        } else {
            self.selected_range.end
        }
    }

    fn previous_boundary(&self, offset: usize) -> usize {
        self.content
            .grapheme_indices(true)
            .rev()
            .find_map(|(index, _)| (index < offset).then_some(index))
            .unwrap_or(0)
    }

    fn next_boundary(&self, offset: usize) -> usize {
        self.content
            .grapheme_indices(true)
            .find_map(|(index, _)| (index > offset).then_some(index))
            .unwrap_or(self.content.len())
    }

    fn index_for_mouse_position(&self, position: Point<Pixels>) -> usize {
        if self.content.is_empty() {
            return 0;
        }
        let (Some(bounds), Some(line)) = (self.last_bounds.as_ref(), self.last_layout.as_ref())
        else {
            return 0;
        };
        if position.y < bounds.top() {
            return 0;
        }
        if position.y > bounds.bottom() {
            return self.content.len();
        }
        let display_index = line.closest_index_for_x(position.x - bounds.left() + self.scroll_x);
        self.content_offset_for_display(display_index)
    }

    fn display_text(&self) -> SharedString {
        if !self.password || self.content.is_empty() {
            return self.content.clone();
        }
        shared(&"•".repeat(self.content.graphemes(true).count()))
    }

    fn display_offset_for_content(&self, offset: usize) -> usize {
        if !self.password {
            return offset;
        }
        self.content
            .grapheme_indices(true)
            .take_while(|(index, _)| *index < offset)
            .count()
            * "•".len()
    }

    fn content_offset_for_display(&self, offset: usize) -> usize {
        if !self.password {
            return offset.min(self.content.len());
        }
        let ordinal = offset / "•".len();
        self.content
            .grapheme_indices(true)
            .nth(ordinal)
            .map_or(self.content.len(), |(index, _)| index)
    }

    fn offset_from_utf16(&self, offset: usize) -> usize {
        let mut utf8_offset = 0;
        let mut utf16_count = 0;
        for character in self.content.chars() {
            if utf16_count >= offset {
                break;
            }
            utf16_count += character.len_utf16();
            utf8_offset += character.len_utf8();
        }
        utf8_offset
    }

    fn offset_to_utf16(&self, offset: usize) -> usize {
        let mut utf16_offset = 0;
        let mut utf8_count = 0;
        for character in self.content.chars() {
            if utf8_count >= offset {
                break;
            }
            utf8_count += character.len_utf8();
            utf16_offset += character.len_utf16();
        }
        utf16_offset
    }

    fn range_to_utf16(&self, range: &Range<usize>) -> Range<usize> {
        self.offset_to_utf16(range.start)..self.offset_to_utf16(range.end)
    }

    fn range_from_utf16(&self, range: &Range<usize>) -> Range<usize> {
        self.offset_from_utf16(range.start)..self.offset_from_utf16(range.end)
    }

    fn emit_changed_if_needed(&mut self, cx: &mut Context<Self>) {
        if self.content == self.last_emitted_content {
            return;
        }
        self.last_emitted_content = self.content.clone();
        self.revision = self.revision.wrapping_add(1).max(1);
        self.emit(self.bindings.changed, EVENT_CHANGED, false, cx);
    }

    fn emit(&mut self, token: u64, kind: u16, focused: bool, cx: &mut Context<Self>) {
        if token == 0 {
            return;
        }
        let callback = self
            .callbacks
            .control_event
            .expect("callbacks were validated before application startup");
        let event = NativeControlEvent {
            kind,
            flags: u16::from(focused),
            reserved: 0,
            revision: self.revision,
            data: self.content.as_ptr(),
            data_length: i32::try_from(self.content.len()).unwrap_or(i32::MAX),
            reserved2: 0,
        };
        let status = unsafe { callback(self.session_id, token, &event) };
        if status != 0 {
            self.callback_error = Some(status);
            cx.notify();
        }
    }
}

impl EntityInputHandler for ManagedInput {
    fn text_for_range(
        &mut self,
        range_utf16: Range<usize>,
        actual_range: &mut Option<Range<usize>>,
        _: &mut Window,
        _: &mut Context<Self>,
    ) -> Option<String> {
        let range = self.range_from_utf16(&range_utf16);
        actual_range.replace(self.range_to_utf16(&range));
        Some(self.content[range].to_string())
    }

    fn selected_text_range(
        &mut self,
        ignore_disabled_input: bool,
        _: &mut Window,
        _: &mut Context<Self>,
    ) -> Option<UTF16Selection> {
        if self.disabled && !ignore_disabled_input {
            return None;
        }
        Some(UTF16Selection {
            range: self.range_to_utf16(&self.selected_range),
            reversed: self.selection_reversed,
        })
    }

    fn marked_text_range(&self, _: &mut Window, _: &mut Context<Self>) -> Option<Range<usize>> {
        self.marked_range
            .as_ref()
            .map(|range| self.range_to_utf16(range))
    }

    fn unmark_text(&mut self, _: &mut Window, cx: &mut Context<Self>) {
        self.marked_range = None;
        self.emit_changed_if_needed(cx);
    }

    fn replace_text_in_range(
        &mut self,
        range_utf16: Option<Range<usize>>,
        new_text: &str,
        _: &mut Window,
        cx: &mut Context<Self>,
    ) {
        if !self.can_edit() {
            return;
        }
        let range = range_utf16
            .as_ref()
            .map(|range| self.range_from_utf16(range))
            .or(self.marked_range.clone())
            .unwrap_or(self.selected_range.clone());
        let new_text = new_text.replace(['\r', '\n'], " ");
        self.content = shared(
            &(self.content[..range.start].to_owned() + &new_text + &self.content[range.end..]),
        );
        let cursor = range.start + new_text.len();
        self.selected_range = cursor..cursor;
        self.selection_reversed = false;
        self.marked_range = None;
        self.emit_changed_if_needed(cx);
        cx.notify();
    }

    fn replace_and_mark_text_in_range(
        &mut self,
        range_utf16: Option<Range<usize>>,
        new_text: &str,
        new_selected_range_utf16: Option<Range<usize>>,
        _: &mut Window,
        cx: &mut Context<Self>,
    ) {
        if !self.can_edit() {
            return;
        }
        let range = range_utf16
            .as_ref()
            .map(|range| self.range_from_utf16(range))
            .or(self.marked_range.clone())
            .unwrap_or(self.selected_range.clone());
        let new_text = new_text.replace(['\r', '\n'], " ");
        self.content = shared(
            &(self.content[..range.start].to_owned() + &new_text + &self.content[range.end..]),
        );
        self.marked_range =
            (!new_text.is_empty()).then_some(range.start..range.start + new_text.len());
        self.selected_range = new_selected_range_utf16
            .as_ref()
            .map(|selection| self.range_from_utf16(selection))
            .map(|selection| range.start + selection.start..range.start + selection.end)
            .unwrap_or_else(|| {
                let cursor = range.start + new_text.len();
                cursor..cursor
            });
        self.selection_reversed = false;
        cx.notify();
    }

    fn bounds_for_range(
        &mut self,
        range_utf16: Range<usize>,
        bounds: Bounds<Pixels>,
        _: &mut Window,
        _: &mut Context<Self>,
    ) -> Option<Bounds<Pixels>> {
        let line = self.last_layout.as_ref()?;
        let range = self.range_from_utf16(&range_utf16);
        let start = self.display_offset_for_content(range.start);
        let end = self.display_offset_for_content(range.end);
        Some(Bounds::from_corners(
            point(
                bounds.left() + line.x_for_index(start) - self.scroll_x,
                bounds.top(),
            ),
            point(
                bounds.left() + line.x_for_index(end) - self.scroll_x,
                bounds.bottom(),
            ),
        ))
    }

    fn character_index_for_point(
        &mut self,
        position: Point<Pixels>,
        _: &mut Window,
        _: &mut Context<Self>,
    ) -> Option<usize> {
        let bounds = self.last_bounds?;
        let line = self.last_layout.as_ref()?;
        let display_index = line.index_for_x(position.x - bounds.left() + self.scroll_x)?;
        Some(self.offset_to_utf16(self.content_offset_for_display(display_index)))
    }
}

impl Focusable for ManagedInput {
    fn focus_handle(&self, _: &App) -> FocusHandle {
        self.focus_handle.clone()
    }
}

impl Render for ManagedInput {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let theme = *self.theme.borrow();
        if self.focus_subscriptions.is_empty() {
            let focus = self.focus_handle.clone();
            let focused = cx.on_focus(&focus, window, |this, _, cx| {
                this.emit(this.bindings.focus_changed, EVENT_FOCUS_CHANGED, true, cx);
            });
            let blurred = cx.on_blur(&focus, window, |this, _, cx| {
                this.is_selecting = false;
                this.emit(this.bindings.focus_changed, EVENT_FOCUS_CHANGED, false, cx);
            });
            self.focus_subscriptions.extend([focused, blurred]);
        }

        if let Some(status) = self.callback_error {
            return div()
                .size_full()
                .flex()
                .items_center()
                .text_color(rgba(theme.error))
                .child(format!("Managed input event failed with status {status}."))
                .into_any_element();
        }

        div()
            .id(&self.focus_handle)
            .role(Role::TextInput)
            .size_full()
            .min_w_0()
            .flex()
            .items_center()
            .text_color(rgba(theme.text))
            .key_context("GpuiDotnetInput")
            .track_focus(&self.focus_handle)
            .cursor(if self.disabled {
                CursorStyle::Arrow
            } else {
                CursorStyle::IBeam
            })
            .on_action(cx.listener(Self::backspace))
            .on_action(cx.listener(Self::delete))
            .on_action(cx.listener(Self::left))
            .on_action(cx.listener(Self::right))
            .on_action(cx.listener(Self::select_left))
            .on_action(cx.listener(Self::select_right))
            .on_action(cx.listener(Self::select_all))
            .on_action(cx.listener(Self::home))
            .on_action(cx.listener(Self::end))
            .on_action(cx.listener(Self::paste))
            .on_action(cx.listener(Self::cut))
            .on_action(cx.listener(Self::copy))
            .on_action(cx.listener(Self::submit))
            .on_mouse_down(MouseButton::Left, cx.listener(Self::on_mouse_down))
            .on_mouse_up(MouseButton::Left, cx.listener(Self::on_mouse_up))
            .on_mouse_up_out(MouseButton::Left, cx.listener(Self::on_mouse_up))
            .on_mouse_move(cx.listener(Self::on_mouse_move))
            .child(TextElement { input: cx.entity() })
            .into_any_element()
    }
}

struct TextElement {
    input: Entity<ManagedInput>,
}

struct PrepaintState {
    line: Option<ShapedLine>,
    cursor: Option<PaintQuad>,
    selection: Option<PaintQuad>,
    scroll_x: Pixels,
}

impl IntoElement for TextElement {
    type Element = Self;

    fn into_element(self) -> Self::Element {
        self
    }
}

impl Element for TextElement {
    type RequestLayoutState = ();
    type PrepaintState = PrepaintState;

    fn id(&self) -> Option<ElementId> {
        None
    }

    fn source_location(&self) -> Option<&'static core::panic::Location<'static>> {
        None
    }

    fn request_layout(
        &mut self,
        _: Option<&GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        window: &mut Window,
        cx: &mut App,
    ) -> (LayoutId, Self::RequestLayoutState) {
        let mut style = Style::default();
        style.size.width = relative(1.).into();
        style.size.height = window.line_height().into();
        (window.request_layout(style, [], cx), ())
    }

    fn prepaint(
        &mut self,
        _: Option<&GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        bounds: Bounds<Pixels>,
        _: &mut Self::RequestLayoutState,
        window: &mut Window,
        cx: &mut App,
    ) -> Self::PrepaintState {
        let input = self.input.read(cx);
        let content_empty = input.content.is_empty();
        let display_text = if content_empty {
            input.placeholder.clone()
        } else {
            input.display_text()
        };
        let style = window.text_style();
        let theme = *input.theme.borrow();
        let color: gpui::Hsla = if content_empty {
            rgba(theme.text_placeholder).into()
        } else {
            style.color
        };
        let run = TextRun {
            len: display_text.len(),
            font: style.font(),
            color,
            background_color: None,
            underline: None,
            strikethrough: None,
        };

        let marked_range = input.marked_range.as_ref().map(|range| {
            input.display_offset_for_content(range.start)
                ..input.display_offset_for_content(range.end)
        });
        let runs = if let Some(marked_range) = marked_range {
            vec![
                TextRun {
                    len: marked_range.start,
                    ..run.clone()
                },
                TextRun {
                    len: marked_range.end - marked_range.start,
                    underline: Some(UnderlineStyle {
                        color: Some(run.color),
                        thickness: px(1.),
                        wavy: false,
                    }),
                    ..run.clone()
                },
                TextRun {
                    len: display_text.len() - marked_range.end,
                    ..run
                },
            ]
            .into_iter()
            .filter(|run| run.len > 0)
            .collect()
        } else {
            vec![run]
        };
        let font_size = style.font_size.to_pixels(window.rem_size());
        let line = window
            .text_system()
            .shape_line(display_text, font_size, &runs, None);

        let cursor_index = if content_empty {
            0
        } else {
            input.display_offset_for_content(input.cursor_offset())
        };
        let cursor_x = line.x_for_index(cursor_index);
        let mut scroll_x = input.scroll_x;
        let available = bounds.size.width;
        if cursor_x < scroll_x {
            scroll_x = cursor_x;
        } else if cursor_x > scroll_x + available - px(2.) {
            scroll_x = cursor_x - available + px(2.);
        }
        let maximum = if line.width > available {
            line.width - available
        } else {
            px(0.)
        };
        if scroll_x > maximum {
            scroll_x = maximum;
        }
        if scroll_x < px(0.) {
            scroll_x = px(0.);
        }

        let origin_x = bounds.left() - scroll_x;
        let selected_range = input.display_offset_for_content(input.selected_range.start)
            ..input.display_offset_for_content(input.selected_range.end);
        let (selection, cursor) = if selected_range.is_empty() {
            (
                None,
                Some(fill(
                    Bounds::new(
                        point(origin_x + cursor_x, bounds.top()),
                        size(px(1.5), bounds.size.height),
                    ),
                    rgba(theme.accent),
                )),
            )
        } else {
            (
                Some(fill(
                    Bounds::from_corners(
                        point(
                            origin_x + line.x_for_index(selected_range.start),
                            bounds.top(),
                        ),
                        point(
                            origin_x + line.x_for_index(selected_range.end),
                            bounds.bottom(),
                        ),
                    ),
                    rgba((theme.accent & 0xFFFFFF00) | 0x40),
                )),
                None,
            )
        };
        PrepaintState {
            line: Some(line),
            cursor,
            selection,
            scroll_x,
        }
    }

    fn paint(
        &mut self,
        _: Option<&GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        bounds: Bounds<Pixels>,
        _: &mut Self::RequestLayoutState,
        prepaint: &mut Self::PrepaintState,
        window: &mut Window,
        cx: &mut App,
    ) {
        let focus_handle = self.input.read(cx).focus_handle.clone();
        window.handle_input(
            &focus_handle,
            ElementInputHandler::new(bounds, self.input.clone()),
            cx,
        );
        if let Some(selection) = prepaint.selection.take() {
            window.paint_quad(selection);
        }
        let line = prepaint
            .line
            .take()
            .expect("line was shaped during prepaint");
        line.paint(
            point(bounds.left() - prepaint.scroll_x, bounds.top()),
            window.line_height(),
            TextAlign::Left,
            None,
            window,
            cx,
        )
        .expect("input line paint failed");
        if focus_handle.is_focused(window)
            && let Some(cursor) = prepaint.cursor.take()
        {
            window.paint_quad(cursor);
        }
        self.input.update(cx, |input, _| {
            input.last_layout = Some(line);
            input.last_bounds = Some(bounds);
            input.scroll_x = prepaint.scroll_x;
        });
    }
}

fn shared(value: &str) -> SharedString {
    SharedString::new(Arc::<str>::from(value))
}

fn single_line(value: &str) -> SharedString {
    if value.contains(['\r', '\n']) {
        shared(&value.replace(['\r', '\n'], " "))
    } else {
        shared(value)
    }
}
