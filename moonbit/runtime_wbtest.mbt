///|
fn fixture() -> (App, Session) {
  let app = App::new()
  let session = new_session(7UL, "test", 800.0, 600.0, 0, fn(ui) { ui.div() }, fn(_) {}, fn(_, _) {})
  session.open = true
  app.sessions.set(7UL, session)
  (app, session)
}

///|
test "owner identities cannot be forged through generic operations" {
  let (_, session) = fixture()
  let frame = new_frame(session, false)
  let root = frame.div().op_u32(@protocol.OP_RESOURCE_OWNER, 2U)
  assert_true(frame.finish(root) is Err(_))
}

///|
test "accepted roots prune removed datasource keys without recycling tokens" {
  let (app, session) = fixture()
  session.renderer_keys.set("removed", 0x100000001UL)
  session.next_renderer = 2UL
  session.pending = Some({ id: 1UL, events: Map([]), ranges: Map([]) })
  assert_eq(app.complete_root(session, 1UL, 0), 0)
  assert_true(session.renderer_keys.is_empty())
  assert_eq(session.next_renderer, 2UL)
}


///|
test "frame emits a little-endian packet and expires its elements" {
  let (_, session) = fixture()
  let frame = new_frame(session, false)
  let root = frame.div().child(frame.text("hello"))
  let bytes = match frame.finish(root) { Ok(bytes) => bytes; Err(error) => fail(error.message) }
  assert_eq(bytes[0], b'G')
  assert_eq(bytes[1], b'M')
  assert_eq(bytes[2], b'B')
  assert_eq(bytes[3], b'1')
  assert_eq(bytes[4], b'\x02')
  assert_true(frame.finished)
  ignore(root.width_px(100.0))
  assert_true(frame.failure is Some(_))
}

///|
test "foreign frame elements and disconnected nodes fail before FFI" {
  let (_, session) = fixture()
  let a = new_frame(session, false)
  let b = new_frame(session, false)
  let root = a.div().child(b.text("wrong frame"))
  assert_true(a.finish(root) is Err(_))
  let frame = new_frame(session, false)
  let root = frame.div()
  ignore(frame.text("orphan"))
  assert_true(frame.finish(root) is Err(_))
}

///|
test "capability checks and item resource checks precede serialization" {
  let (_, session) = fixture()
  let frame = new_frame(session, false)
  let text = frame.text("text").on_click(fn(_, _) {})
  assert_true(frame.finish(text) is Err(_))
  let items = new_frame(session, true)
  let input = items.input("editor")
  assert_true(items.finish(input) is Err(_))
}

///|
test "root callbacks commit only on the matching acknowledgement" {
  let (app, session) = fixture()
  let old : Map[UInt64, Binding] = Map([])
  let fresh : Map[UInt64, Binding] = Map([])
  old.set(1UL, { callback: fn(_, _) {} })
  fresh.set(2UL, { callback: fn(_, _) {} })
  session.events = old
  session.pending = Some({ id: 10UL, events: fresh, ranges: Map([]) })
  assert_true(session.events.contains(1UL))
  assert_true(!session.events.contains(2UL))
  assert_eq(app.complete_root(session, 10UL, 0), 0)
  assert_true(!session.events.contains(1UL))
  assert_true(session.events.contains(2UL))
  assert_true(session.pending is None)
}

///|
test "rejected root retains old bindings and records the native error" {
  let (app, session) = fixture()
  session.events.set(1UL, { callback: fn(_, _) {} })
  session.pending = Some({ id: 10UL, events: Map([]), ranges: Map([]) })
  assert_eq(app.complete_root(session, 10UL, -77), -77)
  assert_true(session.events.contains(1UL))
  assert_true(session.fault is Some(_))
}

///|
test "range callbacks become live on accept and survive root replacement until release" {
  let (app, session) = fixture()
  let events : Map[UInt64, Binding] = Map([])
  events.set(90UL, { callback: fn(_, _) {} })
  session.artifacts.set(12UL, { source: 42UL, events, accepted: false })
  assert_true(!session.artifact_events.contains(90UL))
  assert_eq(app.accept_range(session, 42UL, 12UL), 0)
  assert_true(session.artifact_events.contains(90UL))
  session.pending = Some({ id: 13UL, events: Map([]), ranges: Map([]) })
  assert_eq(app.complete_root(session, 13UL, 0), 0)
  assert_true(session.artifact_events.contains(90UL))
  assert_eq(app.release_range(session, 42UL, 12UL), 0)
  assert_true(!session.artifact_events.contains(90UL))
  assert_eq(app.release_range(session, 42UL, 12UL), 0)
}

///|
test "release remains legal during a pending root and after fault" {
  let (app, session) = fixture()
  session.artifacts.set(12UL, { source: 42UL, events: Map([]), accepted: false })
  session.pending = Some({ id: 13UL, events: Map([]), ranges: Map([]) })
  session.fault = Some(ui_error(-77, "test fault"))
  assert_eq(app.release_range(session, 42UL, 12UL), 0)
  assert_true(session.artifacts.is_empty())
}

///|
test "event context expires and token identities are not reused" {
  let (app, session) = fixture()
  let captured : Ref[EventContext?] = { val: None }
  session.events.set(1UL, { callback: fn(ctx, _) { captured.val = Some(ctx) } })
  assert_eq(app.deliver(session, 1UL, Click({ x: 0.0, y: 0.0, buttons: 0U, modifiers: 0U, payload: 0UL })), 0)
  match captured.val {
    Some(context) => { context.invalidate(); assert_true(context.failure is Some(_)) }
    None => fail("callback was not called")
  }
  let a = new_frame(session, false)
  ignore(a.button("a", "a").on_click(fn(_, _) {}))
  let next = session.next_event
  let b = new_frame(session, false)
  ignore(b.button("b", "b").on_click(fn(_, _) {}))
  assert_eq(session.next_event, next + 1UL)
}

///|
test "cleanup revokes command routes before user cleanup" {
  let (app, session) = fixture()
  let cleaned : Ref[Bool] = { val: false }
  session.closed = Some(fn(_) {
    assert_false_no_raise(app.sessions.contains(7UL), cleaned)
  })
  app.retire_session(session, 0)
  assert_true(cleaned.val)
  assert_true(session.render is None)
  assert_true(session.closed is None)
  assert_true(session.artifact_events.is_empty())
}

///| Tests may not introduce a raising closure into a production non-raising callback type.
fn assert_false_no_raise(value : Bool, result : Ref[Bool]) -> Unit { result.val = !value }

///|
test "menu acknowledgement releases only older generations" {
  let app = App::new()
  let old : Map[UInt64, () -> Unit] = Map([])
  let current : Map[UInt64, () -> Unit] = Map([])
  let pending : Map[UInt64, () -> Unit] = Map([])
  old.set(1UL, fn() {})
  current.set(2UL, fn() {})
  pending.set(3UL, fn() {})
  app.pending_menus.set(1UL, old)
  app.pending_menus.set(2UL, current)
  app.pending_menus.set(3UL, pending)
  app.menu_actions.set(1UL, fn() {})
  app.menu_actions.set(2UL, fn() {})
  app.menu_actions.set(3UL, fn() {})
  assert_eq(app.accept_menu(2UL), 0)
  assert_true(!app.menu_actions.contains(1UL))
  assert_true(app.menu_actions.contains(2UL))
  assert_true(app.menu_actions.contains(3UL))
}

///|
test "list controller rejects invalid keys and ranges before native" {
  let (app, _session) = fixture()
  let window : Window = { app, id: 7UL }
  assert_true(window.list_controller("") is Err(_))
  let controller = match window.list_controller("rows") {
    Ok(controller) => controller
    Err(_) => fail("valid list key")
  }
  assert_true(controller.refresh(-1, 1) is Err(_))
  assert_true(controller.refresh(0, -1) is Err(_))
  assert_true(controller.refresh_ranges([]) is Err(_))
  assert_true(controller.refresh_ranges([(0, -1)]) is Err(_))
  assert_true(controller.splice(-1, 0, 0) is Err(_))
  assert_true(controller.splice(0, -1, 0) is Err(_))
  assert_true(controller.splice(0, 0, -1) is Err(_))
  assert_true(controller.reset(-1) is Err(_))
  assert_true(controller.scroll_to_item(-1) is Err(_))
}
