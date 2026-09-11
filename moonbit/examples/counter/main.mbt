///|
extern "C" fn path_length() -> Int = "gpui_example_path_length"

///|
#borrow(buffer)
extern "C" fn path_copy(buffer : FixedArray[Byte], length : Int) -> Int = "gpui_example_path_copy"

///|
extern "C" fn exit_process(code : Int) -> Unit = "gpui_example_exit"

///|
fn run_sample(path : String) -> Result[Unit, @gpui.UiError] {
  let app = @gpui.App::new()
  let count : Ref[Int] = { val: 0 }
  let name : Ref[String] = { val: "MoonBit" }
  let revision : Ref[UInt64] = { val: 1UL }
  let window = match app.window("MoonBit + GPUI", fn(ui) {
    println("[root render]")
    // Snapshot values belong to this render, not to a native C closure.
    let current = count.val
    let greeting = name.val
    let snapshot_revision = revision.val
    // Static chrome renders once per window; handlers stay live through retained bindings.
    let header = ui.section("header", 1UL, fn(sub) {
      sub.div().v_stack().gap_px(4.0)
        .child(sub.text("MoonBit + GPUI"))
        .child(sub.text("Static section: renders once per window"))
    })
    let status = ui.text("Hello, \{greeting}! Count: \{current}")
    let controls = ui.section("controls", 1UL, fn(sub) {
      sub.div().v_stack().gap_px(8.0).padding_px(12.0)
        .child(sub.button("increment", "+1").on_click(fn(ctx, _) {
          count.val = count.val + 1
          revision.val = revision.val + 1UL
          ctx.invalidate()
        }))
        .child(sub.button("reset", "Reset").on_click(fn(ctx, _) {
          count.val = 0
          revision.val = revision.val + 1UL
          ctx.invalidate()
        }))
        // Scoped native refresh plus a section revision bump so range closures re-capture.
        .child(sub.button("refresh-top", "Refresh top").on_click(fn(ctx, _) {
          match ctx.window().list_controller("rows") {
            Err(_) => ctx.fail("Missing rows list")
            Ok(ctl) => {
              ctx.check(ctl.refresh(0, 10))
              revision.val = revision.val + 1UL
              ctx.invalidate()
            }
          }
        }))
    })
    // Retained controls stay root-level: native control state follows per-frame declaration.
    let name_field = ui.input("name", initial="MoonBit", placeholder="Name")
      .on_input_changed(fn(ctx, value, _) {
        name.val = value
        ctx.invalidate()
      })
    // List data re-renders only when its revision advances; batches stay native-cached otherwise.
    let rows = ui.section("row-list", snapshot_revision, fn(sub) {
      sub.list("rows", 10000, snapshot_revision, fn(items, start, length) {
        Array::makei(length, fn(offset) {
          let index = start + offset
          items.div().v_stack().gap_px(4.0).padding_px(8.0)
            .child(items.text("Row \{index} / count \{current}"))
            .child(items.button("row-\{index}", "Select").height_px(30.0).on_click(fn(ctx, _) {
              count.val = index
              revision.val = revision.val + 1UL
              ctx.invalidate()
            }))
        })
      }).height_px(300.0).width_percent(100.0).list_estimated_item_extent_px(30.0)
    })
    ui.div().v_stack().gap_px(12.0).padding_px(24.0)
      .width_percent(100.0).height_percent(100.0)
      .child(header)
      .child(status)
      .child(controls)
      .child(name_field)
      .child(rows)
  }) {
    Ok(window) => window
    Err(error) => return Err(error)
  }
  app.check(app.on_start(fn(application) {
    application.check(application.set_menu([
      @gpui.Menu("Application", [
        @gpui.Action("Increment", fn() {
          count.val = count.val + 1
          revision.val = revision.val + 1UL
          application.check(window.invalidate())
        }),
        @gpui.Separator,
        @gpui.Action("Close", fn() { application.check(window.close()) }),
      ]),
    ]))
  }))
  app.run(path)
}


///|
fn main {
  let length = path_length()
  if length <= 0 {
    println("Set GPUI_MOON_NATIVE to the native host's absolute path, or use tools/moonbit.py run --library PATH")
    exit_process(2)
    return
  }
  let buffer = FixedArray::make(length, b'\x00')
  if path_copy(buffer, length) != 0 { exit_process(2); return }
  let path = @utf8.decode_lossy(Bytes::from_array(buffer[:])[:])
  match run_sample(path) {
    Ok(_) => ()
    Err(error) => { println(error); exit_process(1) }
  }
}
