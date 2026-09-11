///| Application-wide menu declaration. Native filters stale platform actions before acknowledgement.
pub(all) enum MenuEntry {
  Menu(String, Array[MenuEntry])
  Action(String, () -> Unit)
  Separator
}


///|
priv struct MenuRecord {
  parent : UInt64
  kind : Int
  action : UInt64
  title : Bytes
}

///|
fn collect_menu(app : App, entries : Array[MenuEntry], parent : UInt64, depth : Int,
  records : Array[MenuRecord], actions : Map[UInt64, () -> Unit]) -> Result[Unit, UiError] {
  if depth > 32 { return Err(ui_error(@ffi.ERR_LIMIT, "Menu depth exceeds 32")) }
  for entry in entries {
    if records.length() >= 4096 { return Err(ui_error(@ffi.ERR_LIMIT, "Menu record limit exceeded")) }
    match entry {
      Menu(title, children) => {
        let index = records.length()
        records.push({ parent, kind: 1, action: 0UL, title: encode_text(title) })
        match collect_menu(app, children, unsigned(index), depth + 1, records, actions) {
          Err(error) => return Err(error)
          Ok(_) => ()
        }
      }
      Action(title, callback) => {
        if parent == 0xffffffffUL || app.next_action == 0xffffffffffffffffUL {
          return Err(ui_error(@ffi.ERR_ARGUMENT, "Menu actions require a parent and a fresh identity"))
        }
        let action = app.next_action
        app.next_action = action + 1UL
        actions.set(action, callback)
        records.push({ parent, kind: 2, action, title: encode_text(title) })
      }
      Separator => {
        if parent == 0xffffffffUL { return Err(ui_error(@ffi.ERR_ARGUMENT, "Top-level separator")) }
        records.push({ parent, kind: 3, action: 0UL, title: b"" })
      }
    }
  }
  Ok(())
}

///| Stage a menu transaction. Closures survive until native acknowledges the replacement.
pub fn App::set_menu(self : App, entries : Array[MenuEntry]) -> Result[Unit, UiError] {
  if self.phase != 1 || !self.started || self.rendering {
    return Err(ui_error(@ffi.ERR_RENDER_EFFECT, "Set menus from startup or an event, not render"))
  }
  if self.pending_menus.length() >= 64 || self.next_menu == 0xffffffffffffffffUL {
    return Err(ui_error(@ffi.ERR_LIMIT, "Menu generations exhausted or too many pending updates"))
  }
  let host = match self.host { Some(host) => host; None => return Err(ui_error(@ffi.ERR_STATE, "Missing host")) }
  let records : Array[MenuRecord] = []
  let actions : Map[UInt64, () -> Unit] = Map([])
  match collect_menu(self, entries, 0xffffffffUL, 1, records, actions) {
    Err(error) => return Err(error)
    Ok(_) => ()
  }
  let mut text_size = 0
  for record in records {
    if has_nul(record.title) || record.title.length() > 1048576 - text_size {
      return Err(ui_error(@ffi.ERR_ARGUMENT, "Invalid menu title or text budget exceeded"))
    }
    text_size = text_size + record.title.length()
  }
  let data_start = 4 + records.length() * 24
  let buffer = FixedArray::make(data_start + text_size, b'\x00')
  write_le(buffer, 0, unsigned(records.length()), 4)
  let mut text_offset = 0
  for i = 0; i < records.length(); i = i + 1 {
    let r = records[i]
    let offset = 4 + i * 24
    write_le(buffer, offset, r.parent, 4)
    write_le(buffer, offset + 4, unsigned(r.kind), 2)
    write_le(buffer, offset + 8, r.action, 8)
    write_le(buffer, offset + 16, unsigned(text_offset), 4)
    write_le(buffer, offset + 20, unsigned(r.title.length()), 4)
    copy_bytes(buffer, data_start + text_offset, r.title)
    text_offset = text_offset + r.title.length()
  }
  let generation = self.next_menu
  self.next_menu = generation + 1UL
  self.pending_menus.set(generation, actions)
  for (id, callback) in actions.iter() { self.menu_actions.set(id, callback) }
  let packet = Bytes::from_array(buffer[:])
  let status = @ffi.menu(host, 1UL, generation, packet, packet.length())
  if status != 0 {
    for id in actions.keys() { self.menu_actions.remove(id) }
    self.pending_menus.remove(generation)
  }
  status_result(status, "set application menu")
}

///|
fn App::accept_menu(self : App, generation : UInt64) -> Int {
  if !self.pending_menus.contains(generation) {
    self.record_failure(ui_error(@ffi.ERR_PUBLICATION, "Unknown menu acknowledgement"))
    return @ffi.ERR_PUBLICATION
  }
  let retired : Array[UInt64] = []
  for old in self.pending_menus.keys() { if old < generation { retired.push(old) } }
  for old in retired {
    match self.pending_menus.get(old) {
      Some(actions) => for id in actions.keys() { self.menu_actions.remove(id) }
      None => ()
    }
    self.pending_menus.remove(old)
  }
  0
}
