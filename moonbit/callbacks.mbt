///|
fn Session::allocate_publication(self : Session) -> Result[UInt64, UiError] {
  if self.next_publication == 0xffffffffffffffffUL {
    return Err(ui_error(@ffi.ERR_LIMIT, "Publication identities exhausted"))
  }
  let id = self.next_publication
  self.next_publication = id + 1UL
  Ok(id)
}


///|
fn App::render_root(self : App, session : Session, request : @ffi.Request) -> Int {
  if session.pending is Some(_) {
    return self.fault_session(session, ui_error(@ffi.ERR_PUBLICATION, "Root render before acknowledgement"))
  }
  let render = match session.render { Some(render) => render; None => return @ffi.ERR_RETIRED }
  let id = match session.allocate_publication() {
    Ok(id) => id
    Err(error) => return self.fault_session(session, error)
  }
  let frame = new_frame(session, false)
  self.rendering = true
  let root = render(frame)
  self.rendering = false
  match frame.finish(root) {
    Err(error) => self.fault_session(session, error)
    Ok(packet) => {
      let status = @ffi.reply(request, packet, packet.length(), id)
      if status != 0 {
        self.fault_session(session, ui_error(status, "Native rejected the root declaration"))
      } else {
        session.pending = Some({ id, events: frame.events, ranges: frame.ranges })
        0
      }
    }
  }
}

///|
fn App::complete_root(self : App, session : Session, id : UInt64, status : Int) -> Int {
  let pending = match session.pending {
    Some(pending) if pending.id == id => pending
    _ => return self.fault_session(session, ui_error(@ffi.ERR_PUBLICATION, "Unknown root acknowledgement"))
  }
  session.pending = None
  if status != 0 {
    return self.fault_session(session, ui_error(status, "Native failed to materialize the root declaration"))
  }
  // Existing virtual artifacts are independently owned, even if their datasource disappeared.
  session.events = pending.events
  session.ranges = pending.ranges
  // Removed keys must not accumulate for the lifetime of a long-running window.
  // Reintroducing a removed list allocates a fresh token, never a retired identity.
  for (key, token) in session.renderer_keys.iter().to_array() {
    if !session.ranges.contains(token) { session.renderer_keys.remove(key) }
  }
  0
}

///|
fn App::render_range(self : App, session : Session, renderer : UInt64, source : UInt64,
  start : UInt, count : UInt, request : @ffi.Request) -> Int {
  if session.pending is Some(_) || source == 0UL || start > 0x7fffffffU || count > 512U {
    return self.fault_session(session, ui_error(@ffi.ERR_PUBLICATION, "Invalid range demand or pending root"))
  }
  let binding = match session.ranges.get(renderer) {
    Some(binding) => binding
    None => return self.fault_session(session, ui_error(@ffi.ERR_CALLBACK, "Unknown datasource token"))
  }
  let first = start.reinterpret_as_int()
  let length = count.reinterpret_as_int()
  if first > binding.count || length > binding.count - first {
    return self.fault_session(session, ui_error(@ffi.ERR_ARGUMENT, "Range demand is outside the declared list"))
  }
  let id = match session.allocate_publication() {
    Ok(id) => id
    Err(error) => return self.fault_session(session, error)
  }
  let frame = new_frame(session, true)
  self.rendering = true
  let items = (binding.render)(frame, first, length)
  let root = frame.div().children(items)
  if items.length() != length { frame.fail("Range renderer must return exactly count items") }
  self.rendering = false
  match frame.finish(root) {
    Err(error) => self.fault_session(session, error)
    Ok(packet) => {
      let status = @ffi.reply(request, packet, packet.length(), id)
      if status != 0 {
        self.fault_session(session, ui_error(status, "Native rejected an item batch"))
      } else {
        session.artifacts.set(id, { source, events: frame.events, accepted: false })
        0
      }
    }
  }
}

///|
fn App::accept_range(self : App, session : Session, source : UInt64, id : UInt64) -> Int {
  let artifact = match session.artifacts.get(id) {
    Some(artifact) if artifact.source == source && !artifact.accepted => artifact
    _ => return self.fault_session(session, ui_error(@ffi.ERR_PUBLICATION, "Unknown/duplicate artifact acceptance"))
  }
  artifact.accepted = true
  for (token, binding) in artifact.events.iter() { session.artifact_events.set(token, binding) }
  0
}

///|
fn App::release_range(self : App, session : Session, source : UInt64, id : UInt64) -> Int {
  match session.artifacts.get(id) {
    None => 0 // Teardown and duplicate release are intentionally idempotent.
    Some(artifact) => {
      if artifact.source != source {
        return self.fault_session(session, ui_error(@ffi.ERR_PUBLICATION, "Artifact source mismatch"))
      }
      for token in artifact.events.keys() { session.artifact_events.remove(token) }
      session.artifacts.remove(id)
      0
    }
  }
}

///|
fn App::deliver(self : App, session : Session, token : UInt64, event : Event) -> Int {
  if session.pending is Some(_) {
    return self.fault_session(session, ui_error(@ffi.ERR_PUBLICATION, "Event delivered before root acknowledgement"))
  }
  let binding = match session.events.get(token) {
    Some(binding) => Some(binding)
    None => session.artifact_events.get(token)
  }
  let binding = match binding {
    Some(binding) => binding
    None => return self.fault_session(session, ui_error(@ffi.ERR_CALLBACK, "Unknown or retired event token"))
  }
  let context : EventContext = { target: { app: self, id: session.id }, active: true, failure: None }
  (binding.callback)(context, event)
  context.active = false
  match context.failure {
    Some(error) => self.fault_session(session, error)
    None => 0
  }
}

///| Closed function: no captured heap pointer is passed to or retained by native code.
fn dispatch_callback(kind : Int, identity : UInt64, a : UInt64, b : UInt64,
  x : UInt, y : UInt, status : Int, request : @ffi.Request) -> Int {
  let app = match running_app.val { Some(app) => app; None => return @ffi.ERR_STATE }
  if kind == @ffi.CB_STARTED {
    if identity != 1UL || app.started { return @ffi.ERR_STATE }
    app.started = true
    let windows = app.sessions.values().to_array()
    for session in windows {
      match app.open_session(session) {
        Err(error) => { app.record_failure(error); return error.code }
        Ok(_) => ()
      }
    }
    let startup = app.startup
    app.startup = None
    match startup { Some(startup) => startup(app); None => () }
    return match app.failure { Some(error) => error.code; None => 0 }
  }
  if kind == @ffi.CB_WINDOW_CLOSED {
    if identity != 1UL { return @ffi.ERR_ARGUMENT }
    match app.sessions.get(a) {
      Some(session) => {
        if status != 0 { app.record_failure(ui_error(status, "Native window closed with an error")) }
        app.retire_session(session, status)
      }
      None => ()
    }
    return 0
  }
  if kind == @ffi.CB_MENU_APPLIED {
    if identity != 1UL { return @ffi.ERR_ARGUMENT }
    return app.accept_menu(a)
  }
  if kind == @ffi.CB_MENU_ACTION {
    if identity != 1UL { return @ffi.ERR_ARGUMENT }
    match app.menu_actions.get(a) {
      Some(action) => { action(); return 0 }
      None => return @ffi.ERR_CALLBACK
    }
  }
  let session = match app.sessions.get(identity) {
    Some(session) => session
    None => return if kind == @ffi.CB_RELEASE_ARTIFACT { 0 } else { @ffi.ERR_RETIRED }
  }
  // Release and acknowledgement cleanup is legal after fault or while a root is pending.
  if kind == @ffi.CB_RELEASE_ARTIFACT { return app.release_range(session, a, b) }
  if kind == @ffi.CB_RENDER_COMPLETED { return app.complete_root(session, a, status) }
  match session.fault { Some(error) => return error.code; None => () }
  if kind == @ffi.CB_ACCEPT_ARTIFACT { return app.accept_range(session, a, b) }
  // close is queued. Honor already-scheduled native work until window_closed retires the route;
  // Window::live still rejects new user commands once close was requested.
  if !session.open { return @ffi.ERR_RETIRED }
  if kind == @ffi.CB_RENDER { return app.render_root(session, request) }
  if kind == @ffi.CB_RANGE { return app.render_range(session, a, b, x, y, request) }
  if kind == @ffi.CB_CLICK {
    return app.deliver(session, a, Click({
      x: @ffi.event_x(request), y: @ffi.event_y(request),
      buttons: @ffi.event_buttons(request), modifiers: @ffi.event_modifiers(request), payload: b,
    }))
  }
  if kind == @ffi.CB_CONTROL {
    let length = @ffi.event_data_length(request)
    if length < 0 || length > 64 * 1024 * 1024 { return @ffi.ERR_LIMIT }
    let buffer = FixedArray::make(length, b'\x00')
    let copied = @ffi.event_data_copy(request, buffer, length)
    if copied != 0 { return copied }
    return app.deliver(session, a, Control({
      kind: x.reinterpret_as_int(), flags: y, revision: b, data: Bytes::from_array(buffer[:]),
    }))
  }
  if kind == @ffi.CB_DYNAMIC {
    if session.pending is Some(_) || a != 1UL { return @ffi.ERR_PUBLICATION }
    let context : EventContext = { target: { app, id: identity }, active: true, failure: None }
    match session.dynamic { Some(callback) => callback(context, a.to_uint()); None => () }
    context.active = false
    return match context.failure {
      Some(error) => app.fault_session(session, error)
      None => 0
    }
  }
  @ffi.ERR_CALLBACK
}
