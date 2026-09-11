///| Retained render segments: splice, revision-keyed sections, and handles.
/// See docs/proposals/MOONBIT_RETAINED_TREE.md for scope and limits.
/// Splice one frame's rows into another with base-offset fixups, so a retained section
/// rendered into a scratch sub-frame assembles byte-identically to one-shot rendering.
/// The child frame is expired on success: segments are single-use.
pub fn Frame::append_segment(self : Frame, root : Element) -> Element {
  let invalid : Element = { frame: self, index: -1 }
  if !self.writable() { return invalid }
  let child = root.frame
  if child.identity == self.identity {
    self.fail("Segment must come from a different frame")
    return invalid
  }
  match child.failure {
    Some(error) => {
      if self.failure is None { self.failure = Some(error) }
      return invalid
    }
    None => ()
  }
  if self.item_batch != child.item_batch {
    self.fail("Segment batch kind must match its parent frame")
    return invalid
  }
  if root.index < 0 || root.index >= child.nodes.length() {
    self.fail("Segment root is outside its frame")
    return invalid
  }
  let element = self.absorb_rows(child.nodes, child.ops, child.data, child.edges,
    child.events, child.ranges, root.index)
  if !element.usable() { return invalid }
  for key in child.used_sections { self.record_section_use(key) }
  child.finished = true
  element
}

///| Copy rows with base-offset fixups. Shared by one-shot segment splice and cache reuse.
/// Never mutates the source arrays, so one session-cache entry serves many publications.
fn Frame::absorb_rows(self : Frame, nodes : Array[FrameNode], ops : Array[FrameOp],
  data : Array[Byte], edges : Int, events : Map[UInt64, Binding],
  ranges : Map[UInt64, RangeBinding], root_index : Int) -> Element {
  let invalid : Element = { frame: self, index: -1 }
  if nodes.length() > 1048576 - self.nodes.length() ||
  ops.length() > 1048576 - self.ops.length() ||
  data.length() > 64 * 1024 * 1024 - self.data.length() {
    self.fail("Segment exceeds frame budgets")
    return invalid
  }
  let node_base = self.nodes.length()
  let data_base = self.data.length()
  for byte in data { self.data.push(byte) }
  for n in nodes {
    let children : Array[Int] = []
    for c in n.children { children.push(c + node_base) }
    let parent = if n.parent == -1 { -1 } else { n.parent + node_base }
    self.nodes.push({
      component: n.component, offset: n.offset + data_base, length: n.length,
      children, parent,
    })
  }
  for op in ops {
    let a = if op.kind == 6 { op.a + unsigned(data_base) } else { op.a }
    self.ops.push({ node: op.node + node_base, code: op.code, kind: op.kind, a, b: op.b })
  }
  for (token, binding) in events.iter() { self.events.set(token, binding) }
  for (token, binding) in ranges.iter() {
    if self.ranges.get(token) is Some(_) {
      self.fail("Duplicate list key across segments")
      return invalid
    }
    self.ranges.set(token, binding)
  }
  self.edges = self.edges + edges
  { frame: self, index: node_base + root_index }
}

///|
fn Frame::record_section_use(self : Frame, key : String) -> Unit {
  if !self.used_sections.contains(key) { self.used_sections.push(key) }
}

///| A stable key identifies a retained subtree. Bump revision when its inputs change; a clean
/// section skips its render closure and reuses cached rows. Forbidden inside list items.
/// At most one use per key per publication; the key must be a valid nonempty UTF-8 identity.
pub fn Frame::section(self : Frame, key : String, revision : UInt64,
  render : (Frame) -> Element) -> Element {
  let invalid : Element = { frame: self, index: -1 }
  if !self.writable() { return invalid }
  if self.item_batch {
    self.fail("Sections are not allowed inside a list item")
    return invalid
  }
  if !valid_key(encode_text(key)) {
    self.fail("Invalid section key")
    return invalid
  }
  if self.used_sections.contains(key) {
    self.fail("Duplicate section key in one publication")
    return invalid
  }
  let session = self.session
  match session.sections.get(key) {
    Some(cached) if cached.revision == revision => {
      let element = self.absorb_rows(cached.nodes, cached.ops, cached.data, cached.edges,
        cached.events, cached.ranges, cached.root)
      if !element.usable() { return invalid }
      self.record_section_use(key)
      for nested in cached.used { self.record_section_use(nested) }
      element
    }
    _ => {
      let scratch = new_frame(session, false)
      let root = render(scratch)
      if root.frame.identity != scratch.identity || root.index < 0 ||
      root.index >= scratch.nodes.length() {
        self.fail("Section must return a root from its own frame")
        return invalid
      }
      match scratch.failure {
        Some(error) => {
          if self.failure is None { self.failure = Some(error) }
          return invalid
        }
        None => ()
      }
      let element = self.absorb_rows(scratch.nodes, scratch.ops, scratch.data,
        scratch.edges, scratch.events, scratch.ranges, root.index)
      if !element.usable() { return invalid }
      session.sections.set(key, {
        revision, root: root.index, nodes: scratch.nodes, ops: scratch.ops,
        data: scratch.data, edges: scratch.edges, events: scratch.events,
        ranges: scratch.ranges, used: scratch.used_sections,
      })
      self.record_section_use(key)
      for nested in scratch.used_sections { self.record_section_use(nested) }
      scratch.finished = true
      element
    }
  }
}

///| Imperative handle for one retained section. Dropping the cache entry forces a re-render
/// of that section on the next root render; other sections stay cached.
pub struct SectionHandle {
  window : Window
  key : String
}

///|
pub fn Window::section(self : Window, key : String) -> Result[SectionHandle, UiError] {
  if !valid_key(encode_text(key)) {
    return Err(ui_error(@ffi.ERR_ARGUMENT, "Invalid section key"))
  }
  Ok({ window: self, key })
}

///|
pub fn SectionHandle::invalidate(self : SectionHandle) -> Result[Unit, UiError] {
  match self.window.live() {
    Err(error) => Err(error)
    Ok((_, session)) => {
      session.sections.remove(self.key)
      self.window.invalidate()
    }
  }
}
