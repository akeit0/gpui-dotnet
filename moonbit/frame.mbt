///|
priv struct FrameNode {
  component : Int
  offset : Int
  length : Int
  children : Array[Int]
  mut parent : Int
}


///|
priv struct FrameOp {
  node : Int
  code : Int
  kind : Int
  a : UInt64
  b : UInt64
}

///| A single declaration transaction. Elements and frame writers expire on render return.
struct Frame {
  identity : UInt64
  session : Session
  item_batch : Bool
  nodes : Array[FrameNode]
  ops : Array[FrameOp]
  data : Array[Byte]
  events : Map[UInt64, Binding]
  ranges : Map[UInt64, RangeBinding]
  mut edges : Int
  mut finished : Bool
  mut failure : UiError?
  used_sections : Array[String]
}

///|
struct Element {
  frame : Frame
  index : Int
}

///|
let next_frame_identity : Ref[UInt64] = { val: 1UL }

///|
fn new_frame(session : Session, item_batch : Bool) -> Frame {
  let identity = next_frame_identity.val
  if identity != 0xffffffffffffffffUL { next_frame_identity.val = identity + 1UL }
  let frame : Frame = {
    identity, session, item_batch, nodes: [], ops: [], data: [],
    events: Map([]), ranges: Map([]), edges: 0, finished: false, failure: None,
    used_sections: [],
  }
  if identity == 0xffffffffffffffffUL {
    frame.fail("Frame identities exhausted")
  }
  frame
}

///| Explicit, non-raising failure for user validation during declaration.
pub fn Frame::fail(self : Frame, message : String) -> Unit {
  if self.failure is None {
    self.failure = Some(ui_error(@ffi.ERR_ELEMENT, message))
  }
}

///|
fn Frame::writable(self : Frame) -> Bool {
  if self.finished { self.fail("Frame is already finished"); return false }
  self.failure is None
}

///|
fn Frame::append_data(self : Frame, data : Bytes) -> Int {
  let offset = self.data.length()
  // A frontend budget below the bridge's 256 MiB limit prevents signed arithmetic overflow.
  if data.length() > 64 * 1024 * 1024 - offset {
    self.fail("Frame UTF-8/payload budget exceeded")
    return 0
  }
  for byte in data { self.data.push(byte) }
  offset
}

///|
fn item_component_allowed(component : Int) -> Bool {
  component != @protocol.COMPONENT_SCROLL && component != @protocol.COMPONENT_LIST &&
  component != @protocol.COMPONENT_TABLE && component != @protocol.COMPONENT_INPUT &&
  component != @protocol.COMPONENT_SLIDER && component != @protocol.COMPONENT_DYNAMIC &&
  component != @protocol.COMPONENT_OVERLAY && component != @protocol.COMPONENT_TOOLTIP &&
  component != @protocol.COMPONENT_CONTEXT_MENU && component != @protocol.COMPONENT_POPOVER_MENU &&
  component != @protocol.COMPONENT_DOCK_AREA && component != @protocol.COMPONENT_DOCK_SPLIT &&
  component != @protocol.COMPONENT_DOCK_TABS && component != @protocol.COMPONENT_DOCK_PANEL &&
  component != @protocol.COMPONENT_DOCK_REGION && component != @protocol.COMPONENT_NATIVE_EXTENSION
}

///| Low-level semantic node escape hatch. Data is copied; no raw pointer enters the API.
pub fn Frame::node_bytes(self : Frame, component : Int, data : Bytes) -> Element {
  if !self.writable() { return { frame: self, index: -1 } }
  if !@protocol.component_known(component) ||
     (!@protocol.component_accepts_data(component) && data.length() != 0) ||
     (@protocol.component_requires_data(component) && data.length() == 0) ||
     (self.item_batch && !item_component_allowed(component)) {
    self.fail("Invalid component/payload or retained resource inside a list item")
    return { frame: self, index: -1 }
  }
  if self.nodes.length() >= 1048576 {
    self.fail("Frame node budget exceeded")
    return { frame: self, index: -1 }
  }
  let offset = self.append_data(data)
  let index = self.nodes.length()
  self.nodes.push({ component, offset, length: data.length(), children: [], parent: -1 })
  let element : Element = { frame: self, index }
  let capabilities = @protocol.component_capabilities(component)
  if (capabilities & @protocol.CAPABILITY_INTERACTIVE) != 0UL {
    ignore(element.op_u32(@protocol.OP_ELEMENT_OWNER, 1U))
  }
  if (capabilities & @protocol.CAPABILITY_NATIVE_STATE) != 0UL {
    ignore(element.op_u32(@protocol.OP_RESOURCE_OWNER, 1U))
  }
  element
}

///|
pub fn Frame::node(self : Frame, component : Int, text? : String = "") -> Element {
  self.node_bytes(component, encode_text(text))
}

///|
fn Element::usable(self : Element) -> Bool {
  self.frame.writable() && self.index >= 0 && self.index < self.frame.nodes.length()
}

///|
pub fn Element::child(self : Element, child : Element) -> Element {
  if !self.usable() { return self }
  if child.frame.identity != self.frame.identity || !child.usable() {
    self.frame.fail("Child belongs to another/expired frame")
    return self
  }
  let parent = self.frame.nodes[self.index]
  let node = self.frame.nodes[child.index]
  if !@protocol.component_children(parent.component) || node.parent != -1 || child.index == self.index {
    self.frame.fail("A node must have one parent and the parent must admit children")
    return self
  }
  node.parent = self.index
  parent.children.push(child.index)
  self.frame.edges = self.frame.edges + 1
  self
}

///|
pub fn Element::children(self : Element, children : Array[Element]) -> Element {
  for child in children { ignore(self.child(child)) }
  self
}

///|
fn Element::operation(self : Element, code : Int, kind : Int, a : UInt64, b : UInt64) -> Element {
  if !self.usable() { return self }
  let caps = @protocol.component_capabilities(self.frame.nodes[self.index].component)
  let required = @protocol.operation_capability(code)
  if (code == @protocol.OP_ELEMENT_OWNER || code == @protocol.OP_RESOURCE_OWNER) && a != 1UL {
    self.frame.fail("Owner identities are reserved by the MoonBit runtime")
    return self
  }
  if @protocol.operation_kind(code) != kind || required == 0UL || (caps & required) == 0UL ||
     (kind == 3 && b != 0UL && !@protocol.operation_payload(code)) {
    self.frame.fail("Operation is unknown, has the wrong payload type, or is unsupported by the component")
    return self
  }
  if self.frame.ops.length() >= 1048576 {
    self.frame.fail("Frame operation budget exceeded")
    return self
  }
  self.frame.ops.push({ node: self.index, code, kind, a, b })
  self
}

///|
pub fn Element::op_none(self : Element, code : Int) -> Element {
  self.operation(code, 0, 0UL, 0UL)
}

///|
pub fn Element::op_u32(self : Element, code : Int, value : UInt) -> Element {
  self.operation(code, 2, value.to_uint64(), 0UL)
}

///|
pub fn Element::op_u64(self : Element, code : Int, value : UInt64) -> Element {
  self.operation(code, 4, value, 0UL)
}

///|
pub fn Element::op_f32(self : Element, code : Int, value : Float) -> Element {
  self.operation(code, 1, value.reinterpret_as_uint().to_uint64(), 0UL)
}

///|
pub fn Element::op_f32x2(self : Element, code : Int, x : Float, y : Float) -> Element {
  let bits = x.reinterpret_as_uint().to_uint64() | (y.reinterpret_as_uint().to_uint64() << 32)
  self.operation(code, 5, bits, 0UL)
}

///|
pub fn Element::op_data(self : Element, code : Int, data : Bytes) -> Element {
  if !self.usable() { return self }
  let offset = self.frame.append_data(data)
  self.operation(code, 6, unsigned(offset), unsigned(data.length()))
}

///|
pub fn Element::op_text(self : Element, code : Int, text : String) -> Element {
  self.op_data(code, encode_text(text))
}

///| The closure stays in MoonBit. Its non-reused token is owned by the accepted publication.
pub fn Element::on(self : Element, code : Int, handler : (EventContext, Event) -> Unit,
  payload? : UInt64 = 0UL) -> Element {
  if !self.usable() { return self }
  if code == @protocol.OP_LIST_RENDERER {
    self.frame.fail("Use Frame::list for datasource callbacks")
    return self
  }
  let session = self.frame.session
  if session.next_event > 0x7fffffffUL {
    self.frame.fail("Session event tokens exhausted")
    return self
  }
  let token = 0x0000000180000000UL | session.next_event
  session.next_event = session.next_event + 1UL
  self.frame.events.set(token, { callback: handler })
  self.operation(code, 3, token, payload)
}

///|
pub fn Element::on_click(self : Element, handler : (EventContext, ClickEvent) -> Unit) -> Element {
  self.on(@protocol.OP_ON_CLICK, fn(ctx, event) {
    match event {
      Click(click) => handler(ctx, click)
      _ => ctx.fail("Expected click event")
    }
  })
}

///| Shortcut encoding follows the native packed-command protocol in docs/INTERACTION.md.
pub fn Element::on_shortcut(self : Element, packed_shortcut : UInt64,
  handler : (EventContext) -> Unit) -> Element {
  self.on(@protocol.OP_ON_SHORTCUT, fn(ctx, event) {
    match event {
      Control(control) if control.kind == @protocol.EVENT_SHORTCUT_INVOKED => handler(ctx)
      _ => ctx.fail("Expected shortcut event")
    }
  }, payload=packed_shortcut)
}

///|
pub fn Frame::div(self : Frame) -> Element { self.node(@protocol.COMPONENT_DIV) }

///|
pub fn Frame::text(self : Frame, text : String) -> Element {
  self.node(@protocol.COMPONENT_TEXT, text=text)
}

///|
pub fn Frame::button(self : Frame, key : String, label : String) -> Element {
  if !valid_key(encode_text(key)) { self.fail("Invalid button key") }
  self.node(@protocol.COMPONENT_BUTTON, text=key).child(self.text(label))
}

///|
pub fn Frame::scroll(self : Frame, key : String) -> Element {
  if !valid_key(encode_text(key)) { self.fail("Invalid scroll key") }
  self.node(@protocol.COMPONENT_SCROLL, text=key)
}

///|
pub fn Frame::input(self : Frame, key : String, initial? : String = "",
  placeholder? : String = "") -> Element {
  let k = encode_text(key)
  let value = encode_text(initial)
  let hint = encode_text(placeholder)
  if !valid_key(k) || has_nul(value) || has_nul(hint) ||
     value.length() > 16 * 1024 * 1024 || hint.length() > 1024 * 1024 {
    self.fail("Invalid input key, initial value, or placeholder")
    return { frame: self, index: -1 }
  }
  self.node_bytes(@protocol.COMPONENT_INPUT, composite3(k, value, hint))
}

///|
pub fn Frame::slider(self : Frame, key : String, value : Float,
  minimum? : Float = 0.0, maximum? : Float = 1.0) -> Element {
  if !valid_key(encode_text(key)) { self.fail("Invalid slider key") }
  self.node(@protocol.COMPONENT_SLIDER, text=key)
    .slider_min(minimum).slider_max(maximum).slider_value(value)
}

///| A stable key identifies the datasource. Advance revision when cached item content changes.
/// The range closure must return exactly count element roots and must not perform effects.
pub fn Frame::list(self : Frame, key : String, count : Int, revision : UInt64,
  render : (Frame, Int, Int) -> Array[Element]) -> Element {
  if count < 0 || !valid_key(encode_text(key)) || self.item_batch {
    self.fail("Invalid list key/count or nested list")
  }
  let element = self.node(@protocol.COMPONENT_LIST, text=key)
  if !element.usable() { return element }
  let session = self.session
  let token = match session.renderer_keys.get(key) {
    Some(token) => token
    None => {
      if session.next_renderer > 0x7fffffffUL {
        self.fail("Session datasource tokens exhausted")
        return element
      }
      let token = 0x0000000100000000UL | session.next_renderer
      session.next_renderer = session.next_renderer + 1UL
      session.renderer_keys.set(key, token)
      token
    }
  }
  if self.ranges.get(token) is Some(_) { self.fail("Duplicate list key in one publication") }
  self.ranges.set(token, { render, count })
  element.list_item_count(count.reinterpret_as_uint())
    .list_content_revision(revision).operation(@protocol.OP_LIST_RENDERER, 3, token, 0UL)
}

///|
fn Frame::finish(self : Frame, root : Element) -> Result[Bytes, UiError] {
  if !self.writable() {
    self.finished = true
    return Err(match self.failure { Some(e) => e; None => ui_error(@ffi.ERR_ELEMENT, "Invalid frame") })
  }
  self.finished = true
  if root.frame.identity != self.identity || root.index < 0 || root.index >= self.nodes.length() {
    return Err(ui_error(@ffi.ERR_ELEMENT, "Render must return a root from the current frame"))
  }
  if self.nodes[root.index].parent != -1 || self.edges != self.nodes.length() - 1 {
    return Err(ui_error(@ffi.ERR_ELEMENT, "Render must contain exactly one connected tree"))
  }
  let visited = FixedArray::make(self.nodes.length(), false)
  let stack : Array[(Int, Int)] = [(root.index, 1)]
  let mut seen = 0
  while !stack.is_empty() {
    match stack.pop() {
      Some((index, depth)) => {
        if visited[index] || depth > 128 {
          return Err(ui_error(@ffi.ERR_ELEMENT, "Cycle or tree depth over 128"))
        }
        visited[index] = true
        seen = seen + 1
        for child in self.nodes[index].children { stack.push((child, depth + 1)) }
      }
      None => ()
    }
  }
  if seen != self.nodes.length() {
    return Err(ui_error(@ffi.ERR_ELEMENT, "Disconnected nodes in frame"))
  }
  let node_start = 32
  let op_start = node_start + 12 * self.nodes.length()
  let edge_start = op_start + 24 * self.ops.length()
  let data_start = edge_start + 8 * self.edges
  let output = FixedArray::make(data_start + self.data.length(), b'\x00')
  write_le(output, 0, 0x31424d47UL, 4)
  write_le(output, 4, unsigned(self.nodes.length()), 4)
  write_le(output, 8, unsigned(self.ops.length()), 4)
  write_le(output, 12, unsigned(self.edges), 4)
  write_le(output, 16, unsigned(self.data.length()), 4)
  write_le(output, 20, unsigned(root.index), 4)
  let mut edge_offset = edge_start
  for i = 0; i < self.nodes.length(); i = i + 1 {
    let n = self.nodes[i]
    let offset = node_start + i * 12
    write_le(output, offset, unsigned(n.component), 2)
    write_le(output, offset + 4, unsigned(n.offset), 4)
    write_le(output, offset + 8, unsigned(n.length), 4)
    for child in n.children {
      write_le(output, edge_offset, unsigned(i), 4)
      write_le(output, edge_offset + 4, unsigned(child), 4)
      edge_offset = edge_offset + 8
    }
  }
  for i = 0; i < self.ops.length(); i = i + 1 {
    let op = self.ops[i]
    let offset = op_start + i * 24
    write_le(output, offset, unsigned(op.node), 4)
    write_le(output, offset + 4, unsigned(op.code), 2)
    write_le(output, offset + 6, unsigned(op.kind), 2)
    write_le(output, offset + 8, op.a, 8)
    write_le(output, offset + 16, op.b, 8)
  }
  for i = 0; i < self.data.length(); i = i + 1 { output[data_start + i] = self.data[i] }
  Ok(Bytes::from_array(output[:]))
}
