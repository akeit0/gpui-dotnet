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
  session.pending = Some({ id: 1UL, events: Map([]), ranges: Map([]), sections: [] })
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
  session.pending = Some({ id: 10UL, events: fresh, ranges: Map([]), sections: [] })
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
  session.pending = Some({ id: 10UL, events: Map([]), ranges: Map([]), sections: [] })
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
  session.pending = Some({ id: 13UL, events: Map([]), ranges: Map([]), sections: [] })
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
  session.pending = Some({ id: 13UL, events: Map([]), ranges: Map([]), sections: [] })
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

///|
test "segment splice round-trips one-shot packets" {
  let (_, session_a) = fixture()
  let whole = new_frame(session_a, false)
  let root = whole.div().v_stack().gap_px(2.0)
    .child(whole.text("hello"))
    .child(whole.button("k", "go").on_click(fn(_, _) {}))
  let expected = match whole.finish(root) {
    Ok(bytes) => bytes
    Err(error) => fail(error.message)
  }
  let (_, session_b) = fixture()
  let parent = new_frame(session_b, false)
  let left = new_frame(session_b, false)
  let right = new_frame(session_b, false)
  let head = parent.div().v_stack().gap_px(2.0)
  ignore(head.child(parent.append_segment(left.text("hello"))))
  ignore(head.child(parent.append_segment(right.button("k", "go").on_click(fn(_, _) {}))))
  assert_true(left.finished)
  assert_true(right.finished)
  assert_eq(parent.events.length(), 1)
  let actual = match parent.finish(head) {
    Ok(bytes) => bytes
    Err(error) => fail(error.message)
  }
  assert_eq(expected.length(), actual.length())
  for i in 0..<expected.length() { assert_eq(expected[i], actual[i]) }
}

///|
test "segment splice remaps data payload operations" {
  let (_, session_a) = fixture()
  let whole = new_frame(session_a, false)
  let root = whole.div()
    .child(whole.text("hi").font_family("serif"))
    .child(whole.text("lo").font_family("sans"))
  let expected = match whole.finish(root) {
    Ok(bytes) => bytes
    Err(error) => fail(error.message)
  }
  let (_, session_b) = fixture()
  let parent = new_frame(session_b, false)
  let left = new_frame(session_b, false)
  let right = new_frame(session_b, false)
  let head = parent.div()
  ignore(head.child(parent.append_segment(left.text("hi").font_family("serif"))))
  ignore(head.child(parent.append_segment(right.text("lo").font_family("sans"))))
  let actual = match parent.finish(head) {
    Ok(bytes) => bytes
    Err(error) => fail(error.message)
  }
  assert_eq(expected.length(), actual.length())
  for i in 0..<expected.length() { assert_eq(expected[i], actual[i]) }
}

///|
test "duplicate list keys across segments fail" {
  let (_, session) = fixture()
  let parent = new_frame(session, false)
  let a = new_frame(session, false)
  let first = a.div()
  ignore(first.child(a.list("same", 2, 1UL, fn(items, start, length) {
    Array::makei(length, fn(o) { items.text("r\{start + o}") })
  })))
  let b = new_frame(session, false)
  let second = b.div()
  ignore(second.child(b.list("same", 2, 1UL, fn(items, start, length) {
    Array::makei(length, fn(o) { items.text("r\{start + o}") })
  })))
  let head = parent.div()
  ignore(head.child(parent.append_segment(first)))
  ignore(head.child(parent.append_segment(second)))
  assert_true(parent.failure is Some(_))
}

///|
test "segment splice nests and rejects same-frame roots" {
  let (_, session) = fixture()
  let parent = new_frame(session, false)
  let outer = new_frame(session, false)
  let inner = new_frame(session, false)
  let leaf = inner.text("leaf")
  ignore(outer.div().child(outer.append_segment(leaf)))
  let head = parent.div()
  ignore(head.child(parent.append_segment({ frame: outer, index: 0 })))
  let packet = match parent.finish(head) {
    Ok(bytes) => bytes
    Err(_) => fail("nested splice")
  }
  assert_eq(packet.length() > 32, true)
  let bad = new_frame(session, false)
  let own = bad.div()
  ignore(bad.append_segment(own))
  assert_true(bad.failure is Some(_))
}

///|
test "clean sections skip their render closure with identical packets" {
  let (_, session) = fixture()
  let calls : Ref[Int] = { val: 0 }
  let render = fn(sub : Frame) -> Element {
    calls.val = calls.val + 1
    sub.div().v_stack()
      .child(sub.text("cached"))
      .child(sub.button("b", "go").on_click(fn(_, _) {}))
  }
  let first = new_frame(session, false)
  let head = first.div()
  ignore(head.child(first.section("card", 1UL, render)))
  let expected = match first.finish(head) {
    Ok(bytes) => bytes
    Err(error) => fail(error.message)
  }
  assert_eq(calls.val, 1)
  assert_eq(first.events.length(), 1)
  let second = new_frame(session, false)
  let head2 = second.div()
  ignore(head2.child(second.section("card", 1UL, render)))
  assert_eq(calls.val, 1)
  assert_eq(second.events.length(), 1)
  let actual = match second.finish(head2) {
    Ok(bytes) => bytes
    Err(error) => fail(error.message)
  }
  assert_eq(expected.length(), actual.length())
  for i in 0..<expected.length() { assert_eq(expected[i], actual[i]) }
  let third = new_frame(session, false)
  let head3 = third.div()
  ignore(head3.child(third.section("card", 2UL, render)))
  assert_eq(calls.val, 2)
  ignore(third.finish(head3))
}

///|
test "nested sections reuse transitively" {
  let (_, session) = fixture()
  let outer_calls : Ref[Int] = { val: 0 }
  let inner_calls : Ref[Int] = { val: 0 }
  let inner_render = fn(sub : Frame) -> Element {
    inner_calls.val = inner_calls.val + 1
    sub.text("in")
  }
  let outer_render = fn(sub : Frame) -> Element {
    outer_calls.val = outer_calls.val + 1
    sub.div().child(sub.section("inner", 1UL, inner_render))
  }
  let first = new_frame(session, false)
  let head = first.div()
  ignore(head.child(first.section("outer", 1UL, outer_render)))
  ignore(first.finish(head))
  assert_eq(outer_calls.val, 1)
  assert_eq(inner_calls.val, 1)
  assert_true(session.sections.contains("outer"))
  assert_true(session.sections.contains("inner"))
  let second = new_frame(session, false)
  let head2 = second.div()
  ignore(head2.child(second.section("outer", 1UL, outer_render)))
  ignore(second.finish(head2))
  assert_eq(outer_calls.val, 1)
  assert_eq(inner_calls.val, 1)
}

///|
test "accepted roots prune removed section keys" {
  let (app, session) = fixture()
  session.sections.set("gone", {
    revision: 1UL, root: 0, nodes: [], ops: [], data: [], edges: 0,
    events: Map([]), ranges: Map([]), used: [],
  })
  session.sections.set("keep", {
    revision: 1UL, root: 0, nodes: [], ops: [], data: [], edges: 0,
    events: Map([]), ranges: Map([]), used: [],
  })
  session.pending = Some({ id: 1UL, events: Map([]), ranges: Map([]), sections: ["keep"] })
  assert_eq(app.complete_root(session, 1UL, 0), 0)
  assert_true(!session.sections.contains("gone"))
  assert_true(session.sections.contains("keep"))
}

///|
test "section keys are validated and single-use per publication" {
  let (_, session) = fixture()
  let frame = new_frame(session, false)
  ignore(frame.section("", 1UL, fn(sub) { sub.text("x") }))
  assert_true(frame.failure is Some(_))
  let items = new_frame(session, true)
  ignore(items.section("s", 1UL, fn(sub) { sub.text("x") }))
  assert_true(items.failure is Some(_))
  let ok = new_frame(session, false)
  let head = ok.div()
  ignore(head.child(ok.section("dup", 1UL, fn(sub) { sub.text("x") })))
  ignore(head.child(ok.section("dup", 1UL, fn(sub) { sub.text("x") })))
  assert_true(ok.failure is Some(_))
}

///|
test "section handles validate keys and need a live window" {
  let (app, _session) = fixture()
  let window : Window = { app, id: 7UL }
  assert_true(window.section("") is Err(_))
  let handle = match window.section("card") {
    Ok(handle) => handle
    Err(_) => fail("valid section key")
  }
  assert_true(handle.invalidate() is Err(_))
}
