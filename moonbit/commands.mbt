///| Advanced escape hatch for schema-defined retained resources. Rust validates the payload.
/// The owner is the MoonBit window root (1); callers never supply native pointers.
pub fn Window::resource_command(self : Window, resource : Int, key : String, command : Int,
  data? : Bytes = b"", a? : UInt64 = 0UL, b? : UInt64 = 0UL) -> Result[Unit, UiError] {
  let encoded_key = encode_text(key)
  if !valid_key(encoded_key) { return Err(ui_error(@ffi.ERR_ARGUMENT, "Invalid resource key")) }
  match self.live() {
    Err(error) => Err(error)
    Ok((host, _)) => status_result(@ffi.resource_command(host, self.id, 1U, resource, command,
      encoded_key, encoded_key.length(), data, data.length(), a, b), "resource command")
  }
}


///|
pub fn EventContext::input_focus(self : EventContext, key : String) -> Unit {
  if self.valid() {
    self.check(self.target.resource_command(@protocol.RESOURCE_INPUT, key, @protocol.COMMAND_INPUT_FOCUS))
  }
}

///| Unconditional replacement. Prefer the revision-checked variant for asynchronous results.
pub fn EventContext::input_set_value(self : EventContext, key : String, value : String) -> Unit {
  if self.valid() {
    self.check(self.target.resource_command(@protocol.RESOURCE_INPUT, key,
      @protocol.COMMAND_INPUT_SET_VALUE, data=encode_text(value)))
  }
}

///| Policies occupy only bits 0..1. request=0 omits the result event; otherwise request < 2^62.
/// Delivery still occurs only on the main-thread callback route, not on worker threads.
pub fn EventContext::input_set_if_current(self : EventContext, key : String, value : String,
  revision : UInt64, policies? : UInt = 0U, request? : UInt64 = 0UL) -> Unit {
  if !self.valid() { return }
  if revision == 0UL || policies > 3U || request >= 0x4000000000000000UL {
    self.fail("Invalid conditional input revision/policies/request ID")
    return
  }
  let command = if request == 0UL {
    @protocol.COMMAND_INPUT_SET_VALUE_IF_CURRENT
  } else {
    @protocol.COMMAND_INPUT_SET_VALUE_IF_CURRENT_WITH_RESULT
  }
  self.check(self.target.resource_command(@protocol.RESOURCE_INPUT, key, command,
    data=encode_text(value), a=revision, b=(request << 2) | policies.to_uint64()))
}

///|
pub fn EventContext::slider_set_value(self : EventContext, key : String, value : Float) -> Unit {
  if self.valid() {
    let bits = value.reinterpret_as_uint().to_uint64()
    self.check(self.target.resource_command(@protocol.RESOURCE_SLIDER, key,
      @protocol.COMMAND_SLIDER_SET_VALUE, a=bits << 32))
  }
}

///|
pub fn EventContext::scroll_to_top(self : EventContext, key : String) -> Unit {
  if self.valid() {
    self.check(self.target.resource_command(@protocol.RESOURCE_SCROLL, key, @protocol.COMMAND_SCROLL_TO_TOP))
  }
}

///| Validate a binary control field before reading it. There is no pointer dereference here.
pub fn ControlEvent::read_u64(self : ControlEvent, offset : Int) -> Result[UInt64, UiError] {
  if offset < 0 || offset > self.data.length() || self.data.length() - offset < 8 {
    return Err(ui_error(@ffi.ERR_ARGUMENT, "Control payload is shorter than a u64 field"))
  }
  let mut value = 0UL
  for i = 0; i < 8; i = i + 1 {
    value = value | (self.data[offset + i].to_int().reinterpret_as_uint().to_uint64() << (8 * i))
  }
  Ok(value)
}

///|
pub fn Element::on_input_changed(self : Element,
  handler : (EventContext, String, UInt64) -> Unit) -> Element {
  self.input_on_changed(fn(ctx, event) {
    match event {
      Control(change) if change.kind == @protocol.EVENT_INPUT_CHANGED =>
        handler(ctx, change.text_lossy(), change.revision)
      _ => ctx.fail("Expected input-changed event")
    }
  })
}

///| Stable list identity for scoped batch invalidation. Mirrors the C# ListController
/// key routing: commands target retained measurements/batches by UTF-8 key through the
/// existing resource-command path. Queue a command, then invalidate the root; native
/// commits the queued sequence in one pass and discards only intersecting batches.
pub struct ListController {
  window : Window
  key : String
}

///|
pub fn Window::list_controller(self : Window, key : String) -> Result[ListController, UiError] {
  if !valid_key(encode_text(key)) {
    return Err(ui_error(@ffi.ERR_ARGUMENT, "Invalid list key"))
  }
  Ok({ window: self, key })
}

///|
fn ListController::command(self : ListController, command : Int, a : UInt64,
  b : UInt64) -> Result[Unit, UiError] {
  self.window.resource_command(@protocol.RESOURCE_LIST, self.key, command, a=a, b=b)
}

///| Scroll to an item without changing content. Queues the command; invalidate after.
pub fn ListController::scroll_to_item(self : ListController, index : Int) -> Result[Unit, UiError] {
  if index < 0 { return Err(ui_error(@ffi.ERR_ARGUMENT, "List index must be non-negative")) }
  self.command(@protocol.COMMAND_LIST_SCROLL_TO_ITEM, unsigned(index), 0UL)
}

///| Structural change preserving unaffected measurements. Queues the command; invalidate after.
pub fn ListController::splice(self : ListController, start : Int, removed : Int,
  inserted : Int) -> Result[Unit, UiError] {
  if start < 0 || removed < 0 || inserted < 0 {
    return Err(ui_error(@ffi.ERR_ARGUMENT, "Splice range must be non-negative"))
  }
  let packed = (unsigned(removed) << 32) | unsigned(inserted)
  self.command(@protocol.COMMAND_LIST_SPLICE, unsigned(start), packed)
}

///| Arbitrary reorder/replacement. Queues the command; invalidate after.
pub fn ListController::reset(self : ListController, item_count : Int) -> Result[Unit, UiError] {
  if item_count < 0 { return Err(ui_error(@ffi.ERR_ARGUMENT, "List count must be non-negative")) }
  self.command(@protocol.COMMAND_LIST_RESET, unsigned(item_count), 0UL)
}

///| Invalidate a stable item range without changing count. Queues the command; invalidate after.
pub fn ListController::refresh(self : ListController, start : Int,
  count : Int) -> Result[Unit, UiError] {
  if start < 0 || count < 0 {
    return Err(ui_error(@ffi.ERR_ARGUMENT, "Refresh range must be non-negative"))
  }
  self.command(@protocol.COMMAND_LIST_REFRESH, unsigned(start), unsigned(count))
}

///| Invalidate several stable ranges in one managed update. Queues one command per range.
pub fn ListController::refresh_ranges(self : ListController,
  ranges : Array[(Int, Int)]) -> Result[Unit, UiError] {
  if ranges.is_empty() {
    return Err(ui_error(@ffi.ERR_ARGUMENT, "At least one refresh range is required"))
  }
  for (start, count) in ranges {
    if start < 0 || count < 0 {
      return Err(ui_error(@ffi.ERR_ARGUMENT, "Refresh range must be non-negative"))
    }
  }
  for (start, count) in ranges {
    match self.refresh(start, count) {
      Err(error) => return Err(error)
      Ok(_) => ()
    }
  }
  Ok(())
}

///| Refresh all currently accepted item artifacts. Use list content_revision for data changes.
pub fn Window::invalidate_items(self : Window) -> Result[Unit, UiError] {
  match self.live() {
    Err(error) => Err(error)
    Ok((host, session)) => {
      let keys : Array[(UInt64, UInt64)] = []
      for (id, artifact) in session.artifacts.iter() {
        if artifact.accepted { keys.push((artifact.source, id)) }
      }
      if keys.is_empty() { return Ok(()) }
      if keys.length() > 65536 { return Err(ui_error(@ffi.ERR_LIMIT, "Too many artifacts in one invalidation")) }
      let buffer = FixedArray::make(16 * keys.length(), b'\x00')
      for i = 0; i < keys.length(); i = i + 1 {
        let (source, id) = keys[i]
        write_le(buffer, i * 16, source, 8)
        write_le(buffer, i * 16 + 8, id, 8)
      }
      let packet = Bytes::from_array(buffer[:])
      status_result(@ffi.invalidate_artifacts(host, self.id, packet, packet.length()), "invalidate item artifacts")
    }
  }
}

///| Generic application-level command for theme/cache payloads and native window operations.
/// No arbitrary-thread transport is implied; call from startup/events on the main thread.
pub fn App::application_command(self : App, command : Int, data? : Bytes = b"") -> Result[Unit, UiError] {
  if self.phase != 1 || !self.started || self.rendering {
    return Err(ui_error(@ffi.ERR_RENDER_EFFECT, "Application command outside the effect phase"))
  }
  match self.host {
    Some(host) => status_result(@ffi.application_command(host, 1UL, 0UL, command, 0,
      data, data.length(), 0.0, 0.0, 0.0, 0.0), "application command")
    None => Err(ui_error(@ffi.ERR_STATE, "Missing native host"))
  }
}

///| Record explicit failures from startup/menu callbacks so run returns them to the caller.
pub fn App::check(self : App, result : Result[Unit, UiError]) -> Unit {
  match result { Err(error) => self.record_failure(error); Ok(_) => () }
}
