///| The only global root handed to the closed C trampoline. C never owns a MoonBit object.
let running_app : Ref[App?] = { val: None }

///|
pub fn App::new() -> App {
  {
    sessions: Map([]), next_window: 1UL, phase: 0, started: false, rendering: false,
    host: None, failure: None, startup: None, next_menu: 1UL, next_action: 1UL,
    pending_menus: Map([]), menu_actions: Map([]),
  }
}


///|
fn new_session(id : UInt64, title : String, width : Float, height : Float, title_bar : Int,
  render : (Frame) -> Element, closed : (Int) -> Unit,
  dynamic : (EventContext, UInt) -> Unit) -> Session {
  {
    id, title, width, height, title_bar, render: Some(render), closed: Some(closed),
    dynamic: Some(dynamic), open: false, closing: false, fault: None,
    next_event: 1UL, next_renderer: 1UL, next_publication: 1UL,
    renderer_keys: Map([]), pending: None, events: Map([]), ranges: Map([]),
    sections: Map([]), artifacts: Map([]), artifact_events: Map([]),
  }
}

///| Declare a root before run, or open another window from an event/startup callback.
/// title_bar: 0 = system, 1 = custom content, 2 = hidden; see the native window contract.
pub fn App::window(self : App, title : String, render : (Frame) -> Element,
  width? : Float = 800.0, height? : Float = 600.0, title_bar? : Int = 0,
  on_closed? : (Int) -> Unit = fn(_) {},
  on_frame? : (EventContext, UInt) -> Unit = fn(_, _) {}) -> Result[Window, UiError] {
  if self.phase == 2 || self.rendering {
    return Err(ui_error(@ffi.ERR_RENDER_EFFECT, "Cannot open a window during render or after shutdown"))
  }
  if !(width > 0.0 && width <= 100000.0 && height > 0.0 && height <= 100000.0) ||
     title_bar < 0 || title_bar > 2 || self.next_window == 0xffffffffffffffffUL {
    return Err(ui_error(@ffi.ERR_ARGUMENT, "Invalid window size/style or exhausted window identities"))
  }
  let id = self.next_window
  self.next_window = id + 1UL
  let session = new_session(id, title, width, height, title_bar, render, on_closed, on_frame)
  self.sessions.set(id, session)
  if self.started {
    match self.open_session(session) {
      Err(error) => {
        self.retire_session(session, error.code)
        return Err(error)
      }
      Ok(_) => ()
    }
  }
  Ok({ app: self, id })
}

///| Called once after the application has registered its native command route.
pub fn App::on_start(self : App, handler : (App) -> Unit) -> Result[Unit, UiError] {
  if self.phase != 0 {
    return Err(ui_error(@ffi.ERR_STATE, "Startup must be installed before run"))
  }
  self.startup = Some(handler)
  Ok(())
}

///| Returns the first recorded callback/native failure without throwing across the ABI.
pub fn App::error(self : App) -> UiError? { self.failure }

///|
pub fn Window::id(self : Window) -> UInt64 { self.id }

///|
fn App::open_session(self : App, session : Session) -> Result[Unit, UiError] {
  let host = match self.host {
    Some(host) => host
    None => return Err(ui_error(@ffi.ERR_STATE, "Application has no native host"))
  }
  // Register before enqueueing: a future native render must already find its session.
  session.open = true
  let title = encode_text(session.title)
  let status = @ffi.application_command(host, 1UL, session.id, 1,
    2 | (session.title_bar << 2), title, title.length(), 0.0, 0.0, session.width, session.height)
  if status != 0 { session.open = false }
  status_result(status, "open window")
}

///|
fn App::retire_session(self : App, session : Session, status : Int) -> Unit {
  // Revoke commands and break closure cycles before invoking user cleanup.
  session.open = false
  session.closing = true
  session.render = None
  session.dynamic = None
  session.pending = None
  session.events.clear()
  session.ranges.clear()
  session.sections.clear()
  session.artifacts.clear()
  session.artifact_events.clear()
  session.renderer_keys.clear()
  self.sessions.remove(session.id)
  let cleanup = session.closed
  session.closed = None
  match cleanup { Some(cleanup) => cleanup(status); None => () }
}

///|
fn host_message(host : @ffi.Host) -> String {
  let length = @ffi.host_error_length(host)
  if length <= 0 || length > 4096 { return "Native host operation failed" }
  let buffer = FixedArray::make(length, b'\x00')
  if @ffi.host_error_copy(host, buffer, length) != 0 { return "Native host operation failed" }
  @utf8.decode_lossy(Bytes::from_array(buffer[:])[:])
}

///| Run synchronously on the OS main thread. native_library is an explicit local library path.
/// All callbacks are non-raising; abort/panic is not translated into a recoverable ABI status.
pub fn App::run(self : App, native_library : String) -> Result[Unit, UiError] {
  if self.phase != 0 || running_app.val is Some(_) {
    return Err(ui_error(@ffi.ERR_STATE, "Application is one-shot; another app may already be running"))
  }
  if self.sessions.is_empty() && self.startup is None {
    return Err(ui_error(@ffi.ERR_STATE, "Declare a window or a startup callback before run"))
  }
  let host = @ffi.host_new()
  if @ffi.host_is_null(host) != 0 {
    return Err(ui_error(@ffi.ERR_MEMORY, "Cannot allocate native host"))
  }
  let path = encode_text(native_library)
  let loaded = @ffi.host_load(host, path, path.length(), @protocol.SCHEMA_HASH)
  if loaded != 0 {
    let error = ui_error(loaded, host_message(host))
    @ffi.host_free(host)
    // Loading is recoverable: the caller may correct the path and retry before any native run.
    return Err(error)
  }
  self.host = Some(host)
  self.phase = 1
  running_app.val = Some(self)
  let cb : FuncRef[(Int, UInt64, UInt64, UInt64, UInt, UInt, Int, @ffi.Request) -> Int] =
    fn(kind, identity, a, b, x, y, status, request) {
      dispatch_callback(kind, identity, a, b, x, y, status, request)
    }
  let status = @ffi.host_run(host, 1UL, cb)
  running_app.val = None
  self.phase = 2
  self.started = false
  self.host = None
  self.startup = None
  self.pending_menus.clear()
  self.menu_actions.clear()
  let remaining = self.sessions.values().to_array()
  for session in remaining { self.retire_session(session, status) }
  if status != 0 { self.record_failure(ui_error(status, "Native application stopped with status \{status}")) }
  @ffi.host_free(host)
  match self.failure { Some(error) => Err(error); None => Ok(()) }
}

///|
fn Window::live(self : Window) -> Result[(@ffi.Host, Session), UiError] {
  if self.app.phase != 1 || !self.app.started || self.app.rendering {
    return Err(ui_error(@ffi.ERR_RENDER_EFFECT, "Commands require a running application outside render"))
  }
  let session = match self.app.sessions.get(self.id) {
    Some(session) => session
    None => return Err(ui_error(@ffi.ERR_RETIRED, "Window is retired"))
  }
  if !session.open || session.closing {
    return Err(ui_error(@ffi.ERR_RETIRED, "Window is closing or not open"))
  }
  match session.fault { Some(error) => return Err(error); None => () }
  match self.app.host {
    Some(host) => Ok((host, session))
    None => Err(ui_error(@ffi.ERR_STATE, "Native host is unavailable"))
  }
}

///|
pub fn Window::invalidate(self : Window) -> Result[Unit, UiError] {
  match self.live() {
    Err(error) => Err(error)
    Ok((host, _)) => status_result(@ffi.notify(host, self.id), "invalidate")
  }
}

///|
pub fn Window::close(self : Window) -> Result[Unit, UiError] {
  match self.live() {
    Err(error) => Err(error)
    Ok((host, session)) => {
      let status = @ffi.application_command(host, 1UL, self.id, 2, 0, b"", 0, 0.0, 0.0, 0.0, 0.0)
      if status == 0 { session.closing = true }
      status_result(status, "close window")
    }
  }
}

///|
pub fn Window::set_title(self : Window, title : String) -> Result[Unit, UiError] {
  match self.live() {
    Err(error) => Err(error)
    Ok((host, _)) => {
      let data = encode_text(title)
      status_result(@ffi.application_command(host, 1UL, self.id, 4, 0,
        data, data.length(), 0.0, 0.0, 0.0, 0.0), "set window title")
    }
  }
}

///|
pub fn Window::resize(self : Window, width : Float, height : Float) -> Result[Unit, UiError] {
  match self.live() {
    Err(error) => Err(error)
    Ok((host, _)) => status_result(@ffi.application_command(host, 1UL, self.id, 5, 0,
      b"", 0, 0.0, 0.0, width, height), "resize window")
  }
}

///|
pub fn EventContext::window(self : EventContext) -> Window { self.target }

///|
pub fn EventContext::fail(self : EventContext, message : String) -> Unit {
  if self.failure is None { self.failure = Some(ui_error(@ffi.ERR_CALLBACK, message)) }
}

///| Consume an explicit command result and propagate its failure through the current callback.
pub fn EventContext::check(self : EventContext, result : Result[Unit, UiError]) -> Unit {
  match result {
    Err(error) => if self.failure is None { self.failure = Some(error) }
    Ok(_) => ()
  }
}

///|
fn EventContext::valid(self : EventContext) -> Bool {
  if !self.active { self.fail("Event context has expired"); return false }
  true
}

///|
pub fn EventContext::invalidate(self : EventContext) -> Unit {
  if self.valid() { self.check(self.target.invalidate()) }
}

///|
pub fn EventContext::close(self : EventContext) -> Unit {
  if self.valid() { self.check(self.target.close()) }
}
