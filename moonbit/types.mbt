///| Error codes from Rust are preserved. Bridge/frontend codes occupy -2000..-2099.
pub struct UiError {
  code : Int
  message : String
} derive(Eq, Debug)

///| Manual implementation: `derive(Show)` is deprecated, but `println` still needs `Show`.
pub impl Show for UiError with output(self, logger) {
  logger.write_string("UiError(code=\{self.code}, message=\{self.message})")
}

///|
pub struct ClickEvent {
  x : Float
  y : Float
  buttons : UInt
  modifiers : UInt
  payload : UInt64
}


///| The payload is copied before the native callback returns. It is safe to retain.
pub struct ControlEvent {
  kind : Int
  flags : UInt
  revision : UInt64
  data : Bytes
}

///|
pub enum Event {
  Click(ClickEvent)
  Control(ControlEvent)
}

///| Text convenience only; use data for structured/binary control events.
pub fn ControlEvent::text_lossy(self : ControlEvent) -> String {
  @utf8.decode_lossy(self.data[:])
}

///| A callback-scoped command context. Retaining this value does not extend its validity.
struct EventContext {
  target : Window
  mut active : Bool
  mut failure : UiError?
}

///| A terminal, non-reusable window identity. Holding a handle does not keep a window open.
struct Window {
  app : App
  id : UInt64
}

///|
priv struct Binding {
  callback : (EventContext, Event) -> Unit
}

///|
priv struct RangeBinding {
  render : (Frame, Int, Int) -> Array[Element]
  count : Int
}

///|
priv struct Publication {
  id : UInt64
  events : Map[UInt64, Binding]
  ranges : Map[UInt64, RangeBinding]
}

///|
priv struct Artifact {
  source : UInt64
  events : Map[UInt64, Binding]
  mut accepted : Bool
}

///|
priv struct Session {
  id : UInt64
  title : String
  width : Float
  height : Float
  title_bar : Int
  mut render : ((Frame) -> Element)?
  mut closed : ((Int) -> Unit)?
  mut dynamic : ((EventContext, UInt) -> Unit)?
  mut open : Bool
  mut closing : Bool
  mut fault : UiError?
  mut next_event : UInt64
  mut next_renderer : UInt64
  mut next_publication : UInt64
  renderer_keys : Map[String, UInt64]
  mut pending : Publication?
  mut events : Map[UInt64, Binding]
  mut ranges : Map[UInt64, RangeBinding]
  artifacts : Map[UInt64, Artifact]
  artifact_events : Map[UInt64, Binding]
}

///| One application loop per process, on the OS main thread. No implicit finalizers/unload.
struct App {
  sessions : Map[UInt64, Session]
  mut next_window : UInt64
  mut phase : Int
  mut started : Bool
  mut rendering : Bool
  mut host : @ffi.Host?
  mut failure : UiError?
  mut startup : ((App) -> Unit)?
  mut next_menu : UInt64
  mut next_action : UInt64
  pending_menus : Map[UInt64, Map[UInt64, () -> Unit]]
  menu_actions : Map[UInt64, () -> Unit]
}

///|
fn ui_error(code : Int, message : String) -> UiError {
  { code, message }
}

///|
fn status_result(status : Int, operation : String) -> Result[Unit, UiError] {
  if status == 0 {
    Ok(())
  } else {
    Err(ui_error(status, "\{operation} failed (status \{status})"))
  }
}

///|
fn App::record_failure(self : App, error : UiError) -> Unit {
  if self.failure is None { self.failure = Some(error) }
}

///|
fn App::fault_session(self : App, session : Session, error : UiError) -> Int {
  if session.fault is None { session.fault = Some(error) }
  self.record_failure(error)
  error.code
}
