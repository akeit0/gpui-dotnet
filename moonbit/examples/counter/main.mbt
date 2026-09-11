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
  let scratch : Ref[Int] = { val: 0 }
  // Second session with independent state: invalidating one window never re-renders the other.
  let scratch_window = match app.window("Scratch", fn(ui) {
    let current = scratch.val
    ui.div().v_stack().gap_px(8.0).padding_px(24.0)
      .width_percent(100.0).height_percent(100.0)
      .child(ui.text("Scratch: \{current}"))
      .child(ui.button("scratch-bump", "+1").on_click(fn(ctx, _) {
        scratch.val = scratch.val + 1
        ctx.invalidate()
      }))
  }) {
    Ok(window) => window
    Err(error) => return Err(error)
  }
  let window = match app.window("MoonBit + GPUI", fn(ui) {
    // Snapshot values belong to this render, not to a native C closure.
    let current = count.val
    let greeting = name.val
    let snapshot_revision = revision.val
    let header = ui.div().v_stack().gap_px(4.0)
      .child(ui.text("MoonBit + GPUI"))
      .child(ui.text("Hello, \{greeting}! Count: \{current}"))
    let controls = ui.div().v_stack().gap_px(8.0).padding_px(12.0)
      .child(ui.button("increment", "+1").on_click(fn(ctx, _) {
        count.val = count.val + 1
        revision.val = revision.val + 1UL
        ctx.invalidate()
      }))
      .child(ui.button("reset", "Reset").on_click(fn(ctx, _) {
        count.val = 0
        revision.val = revision.val + 1UL
        ctx.invalidate()
      }))
      // Scoped cross-window update: only the scratch session re-renders.
      .child(ui.button("bump-scratch", "Bump scratch").on_click(fn(ctx, _) {
        scratch.val = scratch.val + 1
        ctx.check(scratch_window.invalidate())
      }))
      .child(ui.input("name", initial="MoonBit", placeholder="Name")
        .on_input_changed(fn(ctx, value, _) {
          name.val = value
          ctx.invalidate()
        }))
    let rows = ui.list("rows", 10000, snapshot_revision, fn(items, start, length) {
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
    ui.div().v_stack().gap_px(12.0).padding_px(24.0)
      .width_percent(100.0).height_percent(100.0)
      .child(header)
      .child(controls)
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
