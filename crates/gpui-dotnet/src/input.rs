use std::{
    ops::Range,
    sync::{
        Arc,
        atomic::{AtomicU64, Ordering},
    },
};

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
    semantic::{
        COMMAND_INPUT_BLUR, COMMAND_INPUT_FOCUS, COMMAND_INPUT_SELECT_ALL, COMMAND_INPUT_SET_VALUE,
        COMMAND_INPUT_SET_VALUE_IF_CURRENT, COMMAND_INPUT_SET_VALUE_IF_CURRENT_WITH_RESULT,
        EVENT_INPUT_CHANGED, EVENT_INPUT_FOCUS_CHANGED, EVENT_INPUT_SUBMITTED,
        EVENT_INPUT_WRITE_COMPLETED,
    },
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
    pub(crate) write_completed: u64,
}

// Wire values are input_write_outcome in bindings/schema.json.
#[repr(u32)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum InputWriteOutcome {
    Applied = 0,
    Unchanged = 1,
    Stale = 2,
    Composing = 3,
}

pub(crate) struct InputWriteCompletion {
    session_id: u64,
    callbacks: ManagedCallbacks,
    token: u64,
    request_id: u64,
    revision: u64,
    outcome: InputWriteOutcome,
}

impl InputWriteCompletion {
    pub(crate) fn emit(self) {
        let mut data = [0u8; 16];
        data[..8].copy_from_slice(&self.request_id.to_le_bytes());
        data[8..12].copy_from_slice(&(self.outcome as u32).to_le_bytes());
        let event = NativeControlEvent {
            kind: EVENT_INPUT_WRITE_COMPLETED,
            flags: 0,
            reserved: 0,
            revision: self.revision,
            data: data.as_ptr(),
            data_length: 16,
            reserved2: 0,
        };
        let callback = self
            .callbacks
            .control_event
            .expect("callbacks validated at startup");
        let status = unsafe { callback(self.session_id, self.token, &event) };
        crate::app_host::after_detached_callback(self.session_id, status);
    }
}

#[derive(Clone, Default, PartialEq)]
pub(crate) struct InputPresentation {
    pub(crate) accessibility: crate::accessibility::Accessibility,
    pub(crate) placeholder: Option<u32>,
    pub(crate) caret: Option<u32>,
    pub(crate) selection: Option<u32>,
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
    presentation: InputPresentation,
    selected_range: Range<usize>,
    selection_reversed: bool,
    marked_range: Option<Range<usize>>,
    last_layout: Option<ShapedLine>,
    last_bounds: Option<Bounds<Pixels>>,
    #[cfg(test)]
    pub(crate) last_paint_color: Option<gpui::Hsla>,
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
            presentation: InputPresentation::default(),
            selected_range: cursor..cursor,
            selection_reversed: false,
            marked_range: None,
            last_layout: None,
            last_bounds: None,
            #[cfg(test)]
            last_paint_color: None,
            scroll_x: px(0.),
            is_selecting: false,
            disabled: initial.disabled,
            read_only: initial.read_only,
            password: initial.password,
            bindings: initial.bindings,
            revision: next_input_revision(),
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
        presentation: InputPresentation,
        cx: &mut Context<Self>,
    ) {
        let placeholder = shared(placeholder);
        let changed = self.placeholder != placeholder
            || self.presentation != presentation
            || self.disabled != disabled
            || self.read_only != read_only
            || self.password != password
            || self.bindings.changed != bindings.changed
            || self.bindings.submitted != bindings.submitted
            || self.bindings.focus_changed != bindings.focus_changed
            || self.bindings.write_completed != bindings.write_completed;
        self.placeholder = placeholder;
        self.presentation = presentation;
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
            COMMAND_INPUT_FOCUS if !self.disabled => self.focus_handle.focus(window, cx),
            COMMAND_INPUT_BLUR if self.focus_handle.is_focused(window) => window.blur(cx),
            COMMAND_INPUT_SET_VALUE => self.set_value(command.data.as_ref(), cx),
            COMMAND_INPUT_SET_VALUE_IF_CURRENT => {
                self.conditional_write(command, cx);
            }
            COMMAND_INPUT_SELECT_ALL if !self.disabled => {
                self.selected_range = 0..self.content.len();
                self.selection_reversed = false;
                cx.notify();
            }
            _ => {}
        }
    }

    fn set_value(&mut self, value: &str, cx: &mut Context<Self>) {
        self.replace_value(value, false, cx);
    }

    pub(crate) fn apply_command_with_result(
        &mut self,
        command: &ResourceCommand,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> Option<InputWriteCompletion> {
        if command.command != COMMAND_INPUT_SET_VALUE_IF_CURRENT_WITH_RESULT {
            self.apply_command(command, window, cx);
            return None;
        }
        let outcome = self.conditional_write(command, cx);
        (self.bindings.write_completed != 0).then_some(InputWriteCompletion {
            session_id: self.session_id,
            callbacks: self.callbacks,
            token: self.bindings.write_completed,
            request_id: command.b >> 2,
            revision: self.revision,
            outcome,
        })
    }

    fn conditional_write(
        &mut self,
        command: &ResourceCommand,
        cx: &mut Context<Self>,
    ) -> InputWriteOutcome {
        if command.a != self.revision {
            return InputWriteOutcome::Stale;
        }
        if self.marked_range.is_some() && command.b & 2 == 0 {
            return InputWriteOutcome::Composing;
        }
        let before = self.revision;
        self.replace_value(command.data.as_ref(), command.b & 1 == 0, cx);
        if before == self.revision {
            InputWriteOutcome::Unchanged
        } else {
            InputWriteOutcome::Applied
        }
    }

    fn replace_value(&mut self, value: &str, preserve_selection: bool, cx: &mut Context<Self>) {
        let content = single_line(value);
        if self.content == content {
            return;
        }
        let selection = preserve_selection.then(|| self.range_to_utf16(&self.selected_range));
        self.update_text_state(content, None);
        self.last_emitted_content = self.content.clone();
        if let Some(selection) = selection {
            let selection = self.range_from_utf16(&selection);
            self.selected_range = self.clamp_grapheme_forward(selection.start)
                ..self.clamp_grapheme_forward(selection.end);
        } else {
            let cursor = self.content.len();
            self.selected_range = cursor..cursor;
            self.selection_reversed = false;
            self.scroll_x = px(0.);
        }
        cx.notify();
    }

    fn clamp_grapheme_forward(&self, offset: usize) -> usize {
        self.content
            .grapheme_indices(true)
            .find(|(index, _)| *index >= offset)
            .map_or(self.content.len(), |(index, _)| index)
    }

    fn update_text_state(&mut self, content: SharedString, marked_range: Option<Range<usize>>) {
        if self.content != content || self.marked_range != marked_range {
            self.revision = next_input_revision();
        }
        self.content = content;
        self.marked_range = marked_range;
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
        if !self.disabled && !self.password && !self.selected_range.is_empty() {
            cx.write_to_clipboard(ClipboardItem::new_string(
                self.content[self.selected_range.clone()].to_string(),
            ));
        }
    }

    fn cut(&mut self, _: &Cut, window: &mut Window, cx: &mut Context<Self>) {
        if !self.can_edit() || self.password || self.selected_range.is_empty() {
            return;
        }
        cx.write_to_clipboard(ClipboardItem::new_string(
            self.content[self.selected_range.clone()].to_string(),
        ));
        self.replace_text_in_range(None, "", window, cx);
    }

    fn submit(&mut self, _: &Submit, _: &mut Window, cx: &mut Context<Self>) {
        if !self.disabled {
            self.emit(self.bindings.submitted, EVENT_INPUT_SUBMITTED, false, cx);
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
        offset_from_utf16(&self.content, offset)
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
        self.emit(self.bindings.changed, EVENT_INPUT_CHANGED, false, cx);
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
        if self.marked_range.take().is_some() {
            self.revision = next_input_revision();
            cx.notify();
        }
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
        let content = shared(
            &(self.content[..range.start].to_owned() + &new_text + &self.content[range.end..]),
        );
        self.update_text_state(content, None);
        let cursor = range.start + new_text.len();
        self.selected_range = cursor..cursor;
        self.selection_reversed = false;
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
        let content = shared(
            &(self.content[..range.start].to_owned() + &new_text + &self.content[range.end..]),
        );
        let marked_range =
            (!new_text.is_empty()).then_some(range.start..range.start + new_text.len());
        self.update_text_state(content, marked_range);
        self.selected_range = new_selected_range_utf16
            .as_ref()
            .map(|selection| {
                range.start + offset_from_utf16(&new_text, selection.start)
                    ..range.start + offset_from_utf16(&new_text, selection.end)
            })
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
                this.emit(
                    this.bindings.focus_changed,
                    EVENT_INPUT_FOCUS_CHANGED,
                    true,
                    cx,
                );
            });
            let blurred = cx.on_blur(&focus, window, |this, _, cx| {
                this.is_selecting = false;
                this.emit(
                    this.bindings.focus_changed,
                    EVENT_INPUT_FOCUS_CHANGED,
                    false,
                    cx,
                );
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
            .map(|element| self.presentation.accessibility.apply(element))
            .size_full()
            .min_w_0()
            .flex()
            .items_center()
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
    #[cfg(test)]
    color: gpui::Hsla,
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
            rgba(
                input
                    .presentation
                    .placeholder
                    .unwrap_or(theme.text_placeholder),
            )
            .into()
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
                    rgba(input.presentation.caret.unwrap_or(theme.accent)),
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
                    rgba(
                        input
                            .presentation
                            .selection
                            .unwrap_or((theme.accent & 0xFFFFFF00) | 0x40),
                    ),
                )),
                None,
            )
        };
        PrepaintState {
            #[cfg(test)]
            color,
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
            #[cfg(test)]
            {
                input.last_paint_color = Some(prepaint.color);
            }
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

// A stale event must not match a newly created resource under the same retained key.
fn next_input_revision() -> u64 {
    static NEXT: AtomicU64 = AtomicU64::new(1);
    NEXT.try_update(Ordering::Relaxed, Ordering::Relaxed, |revision| {
        revision.checked_add(1)
    })
    .expect("input revision space exhausted")
}

fn offset_from_utf16(text: &str, offset: usize) -> usize {
    let mut utf8_offset = 0;
    let mut utf16_count = 0;
    for character in text.chars() {
        if utf16_count >= offset {
            break;
        }
        utf16_count += character.len_utf16();
        utf8_offset += character.len_utf8();
    }
    utf8_offset
}

#[cfg(test)]
mod tests {
    use std::{cell::RefCell, rc::Rc};

    use super::*;
    use crate::{
        abi::ManagedCallbacks,
        resources::{ResourceCommand, ResourceKey},
        theme::NativeTheme,
    };

    fn callbacks() -> ManagedCallbacks {
        ManagedCallbacks {
            struct_size: 0,
            render: None,
            render_completed: None,
            release_artifact: None,
            accept_artifact: None,
            click: None,
            list_render_range: None,
            dynamic_frame: None,
            control_event: None,
            application_started: None,
            window_closed: None,
            menu_action: None,
            menu_applied: None,
            window_placement: None,
            window_opened: None,
            application_ready: None,
        }
    }

    fn theme() -> SharedTheme {
        Rc::new(RefCell::new(NativeTheme::default()))
    }

    fn input_entity(cx: &mut App) -> Entity<ManagedInput> {
        cx.new(|cx| {
            ManagedInput::new(
                1,
                callbacks(),
                InputInitialState {
                    value: "",
                    placeholder: "",
                    disabled: false,
                    read_only: false,
                    password: false,
                    bindings: InputBindings {
                        changed: 0,
                        submitted: 0,
                        focus_changed: 0,
                        write_completed: 0,
                    },
                },
                theme(),
                cx,
            )
        })
    }

    fn set_value_command(data: &str) -> ResourceCommand {
        ResourceCommand {
            key: ResourceKey::new(7, "input".into()),
            resource_kind: crate::semantic::RESOURCE_INPUT,
            command: crate::semantic::COMMAND_INPUT_SET_VALUE,
            a: 0,
            b: 0,
            data: data.into(),
        }
    }

    #[gpui::test]
    fn password_copy_and_cut_leave_clipboard_and_value_unchanged(cx: &mut gpui::TestAppContext) {
        let input = cx.update(input_entity);
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        cx.update(|window, cx| {
            input.update(cx, |input, cx| {
                input.password = true;
                input.content = shared("secret");
                input.selected_range = 0..6;
                cx.write_to_clipboard(ClipboardItem::new_string("sentinel".to_string()));
                input.copy(&Copy, window, cx);
                assert_eq!(
                    cx.read_from_clipboard().and_then(|item| item.text()),
                    Some("sentinel".to_string())
                );
                input.cut(&Cut, window, cx);
                assert_eq!(
                    cx.read_from_clipboard().and_then(|item| item.text()),
                    Some("sentinel".to_string())
                );
                assert_eq!(input.content.as_ref(), "secret");
                assert_eq!(input.selected_range, 0..6);
            });
        });
    }

    #[gpui::test]
    fn presentation_updates_paint_without_changing_editing_state(cx: &mut gpui::TestAppContext) {
        let input = cx.update(input_entity);
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        for (phase, explicit) in [false, true, true, false].into_iter().enumerate() {
            for selected in [false, true] {
                cx.draw(Point::default(), size(px(300.), px(40.)), |window, cx| {
                    input.update(cx, |input, cx| {
                        input.content = shared(if selected { "hello" } else { "" });
                        input.selected_range = if selected { 1..4 } else { 0..0 };
                        input.selection_reversed = selected;
                        input.marked_range = selected.then_some(1..3);
                        input.focus_handle.focus(window, cx);
                        let revision = input.revision;
                        let presentation = if explicit {
                            InputPresentation {
                                accessibility: crate::accessibility::Accessibility {
                                    name: Some("Account".into()),
                                    description: Some("Enter your account name".into()),
                                },
                                placeholder: Some(0x112233FF),
                                caret: Some(0x445566FF),
                                selection: Some(0x77889940),
                            }
                        } else {
                            InputPresentation::default()
                        };
                        input.configure(
                            "hint",
                            false,
                            false,
                            false,
                            input.bindings,
                            presentation,
                            cx,
                        );
                        input.theme.borrow_mut().text_placeholder =
                            0xAABBCCFF + phase as u32 * 0x100;
                        input.theme.borrow_mut().accent = 0xDDEEFFFF - phase as u32 * 0x100;
                        assert_eq!(input.revision, revision);
                        assert_eq!(input.selected_range, if selected { 1..4 } else { 0..0 });
                        assert_eq!(input.selection_reversed, selected);
                        assert_eq!(input.marked_range, selected.then_some(1..3));
                        assert!(input.focus_handle.is_focused(window));
                    });
                    let mut element = TextElement {
                        input: input.clone(),
                    };
                    let painted = element.prepaint(
                        None,
                        None,
                        Bounds::new(Point::default(), size(px(300.), px(24.))),
                        &mut (),
                        window,
                        cx,
                    );
                    let theme = *input.read(cx).theme.borrow();
                    if selected {
                        assert_eq!(
                            painted.selection.unwrap().background,
                            rgba(if explicit {
                                0x77889940
                            } else {
                                (theme.accent & 0xFFFFFF00) | 0x40
                            })
                            .into()
                        );
                        assert!(painted.cursor.is_none());
                    } else {
                        assert_eq!(
                            painted.color,
                            rgba(if explicit {
                                0x112233FF
                            } else {
                                theme.text_placeholder
                            })
                            .into()
                        );
                        assert_eq!(
                            painted.cursor.unwrap().background,
                            rgba(if explicit { 0x445566FF } else { theme.accent }).into()
                        );
                    }
                    div()
                });
            }
        }
    }

    fn conditional_value(data: &str, revision: u64, policies: u64) -> ResourceCommand {
        ResourceCommand {
            command: COMMAND_INPUT_SET_VALUE_IF_CURRENT,
            a: revision,
            b: policies,
            ..set_value_command(data)
        }
    }

    #[gpui::test]
    fn conditional_replacement_rejects_stale_edits_and_controller_writes(
        cx: &mut gpui::TestAppContext,
    ) {
        let input = cx.update(input_entity);
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        cx.update(|window, cx| {
            input.update(cx, |input, cx| {
                let initial = input.revision;
                assert_ne!(initial, 0);
                input.replace_text_in_range(None, "typed", window, cx);
                let typed = input.revision;
                assert_ne!(typed, initial);
                input.apply_command(&conditional_value("stale", initial, 0), window, cx);
                assert_eq!(input.content.as_str(), "typed");
                assert_eq!(input.revision, typed);
                input.apply_command(&conditional_value("accepted", typed, 0), window, cx);
                let replaced = input.revision;
                assert_ne!(replaced, typed);
                assert_eq!(input.content.as_str(), "accepted");
                assert_eq!(input.last_emitted_content, input.content);
                input.apply_command(&conditional_value("second result", typed, 0), window, cx);
                assert_eq!(input.content.as_str(), "accepted");
                input.set_value("unconditional", cx);
                assert_ne!(input.revision, replaced);
                input.apply_command(&conditional_value("stale again", replaced, 3), window, cx);
                assert_eq!(input.content.as_str(), "unconditional");
            })
        });
    }

    #[gpui::test]
    fn conditional_replacement_preserves_utf16_selection_and_clamps_graphemes(
        cx: &mut gpui::TestAppContext,
    ) {
        let input = cx.update(input_entity);
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        cx.update(|window, cx| {
            input.update(cx, |input, cx| {
                input.set_value("日本語", cx);
                input.selected_range = 3..6; // UTF-16 1..2, not UTF-8 1..2.
                input.selection_reversed = true;
                input.scroll_x = px(12.);
                input.apply_command(&conditional_value("abcd", input.revision, 0), window, cx);
                assert_eq!(input.selected_range, 1..2);
                assert!(input.selection_reversed);
                assert_eq!(input.scroll_x, px(12.));
                // Both offsets fall inside this single grapheme (surrogate pair + combining mark).
                input.apply_command(
                    &conditional_value("🙂\u{301}x", input.revision, 0),
                    window,
                    cx,
                );
                assert_eq!(input.selected_range, 6..6);
                input.apply_command(&conditional_value("", input.revision, 0), window, cx);
                assert_eq!(input.selected_range, 0..0);
                input.apply_command(&conditional_value("end", input.revision, 1), window, cx);
                assert_eq!(input.selected_range, 3..3);
                assert!(!input.selection_reversed);
                assert_eq!(input.scroll_x, px(0.));
            })
        });
    }

    #[gpui::test]
    fn composition_revisions_block_async_results_and_require_explicit_cancellation(
        cx: &mut gpui::TestAppContext,
    ) {
        let input = cx.update(input_entity);
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        cx.update(|window, cx| {
            input.update(cx, |input, cx| {
                input.set_value("日a", cx);
                let before = input.revision;
                input.replace_and_mark_text_in_range(Some(1..2), "🙂x", Some(2..3), window, cx);
                let composing = input.revision;
                assert_ne!(composing, before);
                assert_eq!(input.content.as_str(), "日🙂x");
                assert_eq!(input.marked_range, Some(3..8));
                assert_eq!(input.selected_range, 7..8); // Relative to inserted text, not the prefix.
                assert_eq!(input.last_emitted_content.as_str(), "日a");
                input.apply_command(&conditional_value("stale", before, 3), window, cx);
                input.apply_command(&conditional_value("interrupt", composing, 0), window, cx);
                assert_eq!(input.content.as_str(), "日🙂x");
                assert_eq!(input.revision, composing);
                // Even an explicit cancel is a no-op for identical normalized content.
                input.apply_command(&conditional_value("日🙂x", composing, 3), window, cx);
                assert_eq!(input.marked_range, Some(3..8));
                assert_eq!(input.selected_range, 7..8);
                assert_eq!(input.revision, composing);
                input.apply_command(&conditional_value("accepted", composing, 3), window, cx);
                assert_eq!(input.content.as_str(), "accepted");
                assert_eq!(input.marked_range, None);
                assert_eq!(input.selected_range, 8..8);
                let replaced = input.revision;
                assert_ne!(replaced, composing);
                // Starting and finishing composition with unchanged text must also revoke old tokens.
                input.replace_and_mark_text_in_range(Some(0..8), "accepted", None, window, cx);
                let marked = input.revision;
                assert_ne!(marked, replaced);
                input.unmark_text(window, cx);
                assert_ne!(input.revision, marked);
                input.apply_command(&conditional_value("late", replaced, 0), window, cx);
                assert_eq!(input.content.as_str(), "accepted");
            })
        });
    }

    #[gpui::test]
    fn recreated_inputs_do_not_reuse_revision_tokens(cx: &mut gpui::TestAppContext) {
        let old = cx.update(input_entity);
        let revision = cx.read(|cx| old.read(cx).revision);
        drop(old);
        let input = cx.update(input_entity);
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        cx.update(|window, cx| {
            input.update(cx, |input, cx| {
                assert_ne!(input.revision, revision);
                input.apply_command(&conditional_value("old result", revision, 3), window, cx);
                assert_eq!(input.content.as_str(), "");
            })
        });
    }

    #[gpui::test]
    fn conditional_write_results_cover_decision_precedence_without_change_events(
        cx: &mut gpui::TestAppContext,
    ) {
        let input = cx.update(input_entity);
        let (_, window_cx) = cx.add_window_view(|_, _| gpui::Empty);
        window_cx.update(|window, cx| {
            input.update(cx, |input, cx| {
                input.bindings.write_completed = 9;
                input.disabled = true;
                input.read_only = true;
                let initial = input.revision;
                let mut command = conditional_value("abc", initial, 4);
                command.command = COMMAND_INPUT_SET_VALUE_IF_CURRENT_WITH_RESULT;
                let applied = input
                    .apply_command_with_result(&command, window, cx)
                    .unwrap();
                assert_eq!(applied.outcome, InputWriteOutcome::Applied);
                assert_eq!(applied.request_id, 1);
                assert_ne!(applied.revision, initial);
                assert_eq!(input.content.as_str(), "abc");
                assert_eq!(input.last_emitted_content, input.content);

                input.selected_range = 0..2;
                command.a = input.revision;
                let unchanged = input
                    .apply_command_with_result(&command, window, cx)
                    .unwrap();
                assert_eq!(unchanged.outcome, InputWriteOutcome::Unchanged);
                assert_eq!(unchanged.revision, applied.revision);
                assert_eq!(input.selected_range, 0..2);
                input.marked_range = Some(0..2);
                let composing = input
                    .apply_command_with_result(&command, window, cx)
                    .unwrap();
                assert_eq!(composing.outcome, InputWriteOutcome::Composing);
                assert_eq!(input.marked_range, Some(0..2));
                command.a = initial;
                let stale = input
                    .apply_command_with_result(&command, window, cx)
                    .unwrap();
                assert_eq!(stale.outcome, InputWriteOutcome::Stale);

                command.a = input.revision;
                command.b = u64::MAX;
                let equal_composition = input
                    .apply_command_with_result(&command, window, cx)
                    .unwrap();
                assert_eq!(equal_composition.outcome, InputWriteOutcome::Unchanged);
                assert_eq!(equal_composition.request_id, u64::MAX >> 2);
                assert_eq!(input.marked_range, Some(0..2));
                command.data = shared("replacement");
                let replaced = input
                    .apply_command_with_result(&command, window, cx)
                    .unwrap();
                assert_eq!(replaced.outcome, InputWriteOutcome::Applied);
                assert!(input.marked_range.is_none());
                assert_eq!(input.selected_range, 11..11);
                input.bindings.write_completed = 0;
                command.a = input.revision;
                assert!(
                    input
                        .apply_command_with_result(&command, window, cx)
                        .is_none()
                );
            });
        });
    }

    #[gpui::test]
    fn write_completion_captures_scalars_and_defers_callback_until_after_input_borrow(
        cx: &mut gpui::TestAppContext,
    ) {
        static REQUEST: AtomicU64 = AtomicU64::new(0);
        static REVISION: AtomicU64 = AtomicU64::new(0);
        static VALID: AtomicU64 = AtomicU64::new(0);
        unsafe extern "C" fn completed(
            session: u64,
            token: u64,
            event: *const NativeControlEvent,
        ) -> i32 {
            let event = unsafe { &*event };
            let data =
                unsafe { std::slice::from_raw_parts(event.data, event.data_length as usize) };
            REQUEST.store(
                u64::from_le_bytes(data[..8].try_into().unwrap()),
                Ordering::Relaxed,
            );
            REVISION.store(event.revision, Ordering::Relaxed);
            VALID.store(
                u64::from(
                    session == 1
                        && token == 9
                        && event.kind == EVENT_INPUT_WRITE_COMPLETED
                        && event.flags == 0
                        && event.reserved == 0
                        && event.reserved2 == 0
                        && data[8..] == [0; 8],
                ),
                Ordering::Relaxed,
            );
            0
        }
        REQUEST.store(0, Ordering::Relaxed);
        let input = cx.update(input_entity);
        let (_, window_cx) = cx.add_window_view(|_, _| gpui::Empty);
        let mut decision_revision = 0;
        window_cx.update(|window, cx| {
            let result = input.update(cx, |input, cx| {
                input.session_id = 1;
                input.callbacks.control_event = Some(completed);
                input.bindings.write_completed = 9;
                let mut command = conditional_value("new", input.revision, 7 << 2);
                command.command = COMMAND_INPUT_SET_VALUE_IF_CURRENT_WITH_RESULT;
                input
                    .apply_command_with_result(&command, window, cx)
                    .unwrap()
            });
            decision_revision = result.revision;
            assert_eq!(REQUEST.load(Ordering::Relaxed), 0);
            input.update(cx, |input, cx| input.set_value("later edit", cx));
            window.defer(cx, move |_, _| result.emit());
        });
        cx.run_until_parked();
        assert_eq!(REQUEST.load(Ordering::Relaxed), 7);
        assert_eq!(REVISION.load(Ordering::Relaxed), decision_revision);
        assert_eq!(VALID.load(Ordering::Relaxed), 1);
    }

    #[gpui::test]
    fn change_callbacks_carry_current_revision_without_controller_echoes(
        cx: &mut gpui::TestAppContext,
    ) {
        static LAST: AtomicU64 = AtomicU64::new(0);
        static CALLS: AtomicU64 = AtomicU64::new(0);
        unsafe extern "C" fn changed(_: u64, _: u64, event: *const NativeControlEvent) -> i32 {
            LAST.store(unsafe { (*event).revision }, Ordering::Relaxed);
            CALLS.fetch_add(1, Ordering::Relaxed);
            0
        }
        LAST.store(0, Ordering::Relaxed);
        CALLS.store(0, Ordering::Relaxed);
        let input = cx.update(input_entity);
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        cx.update(|window, cx| {
            input.update(cx, |input, cx| {
                input.bindings.changed = 1;
                input.callbacks.control_event = Some(changed);
                input.replace_text_in_range(None, "a", window, cx);
                assert_eq!(LAST.load(Ordering::Relaxed), input.revision);
                assert_eq!(CALLS.load(Ordering::Relaxed), 1);
                input.apply_command(&conditional_value("b", input.revision, 0), window, cx);
                input.set_value("c", cx);
                assert_eq!(CALLS.load(Ordering::Relaxed), 1);
                input.replace_and_mark_text_in_range(Some(0..1), "語", None, window, cx);
                assert_eq!(CALLS.load(Ordering::Relaxed), 1);
                input.unmark_text(window, cx);
                assert_eq!(LAST.load(Ordering::Relaxed), input.revision);
                assert_eq!(CALLS.load(Ordering::Relaxed), 2);
            })
        });
    }

    /// A set-value command must replace content, not fall through to the
    /// focus arm: pattern arms that fail to resolve to constants silently
    /// become catch-all bindings and misroute every later command.
    #[gpui::test]
    fn set_value_command_replaces_content(cx: &mut gpui::TestAppContext) {
        let input = cx.update(input_entity);
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        cx.update(|window, cx| {
            input.update(cx, |input, cx| {
                input.apply_command(&set_value_command("hi"), window, cx)
            });
        });
        cx.read(|cx| assert_eq!(input.read(cx).content.as_str(), "hi"));
    }

    #[gpui::test]
    fn identical_set_value_preserves_editing_state(cx: &mut gpui::TestAppContext) {
        let input = cx.update(input_entity);
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        cx.update(|window, cx| {
            input.update(cx, |input, cx| {
                input.set_value("日本語 ", cx);
                input.selected_range = 3..6;
                input.selection_reversed = true;
                input.marked_range = Some(0..6);
                input.scroll_x = px(12.);
                input.revision = 9;
                input.apply_command(&set_value_command("日本語\n"), window, cx);
                assert_eq!(input.selected_range, 3..6);
                assert!(input.selection_reversed);
                assert_eq!(input.marked_range, Some(0..6));
                assert_eq!(input.scroll_x, px(12.));
                assert_eq!(input.revision, 9);

                input.apply_command(&set_value_command("changed"), window, cx);
                assert_eq!(input.content.as_str(), "changed");
                assert_eq!(input.selected_range, 7..7);
                assert!(!input.selection_reversed);
                assert_eq!(input.marked_range, None);
                assert_eq!(input.scroll_x, px(0.));
            });
        });
    }
}
