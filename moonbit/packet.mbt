///|
fn unsigned(value : Int) -> UInt64 {
  value.reinterpret_as_uint().to_uint64()
}


///|
fn write_le(buffer : FixedArray[Byte], offset : Int, value : UInt64, size : Int) -> Unit {
  for i = 0; i < size; i = i + 1 {
    buffer[offset + i] = (value >> (i * 8)).to_byte()
  }
}

///|
fn copy_bytes(buffer : FixedArray[Byte], offset : Int, bytes : Bytes) -> Unit {
  for i = 0; i < bytes.length(); i = i + 1 {
    buffer[offset + i] = bytes[i]
  }
}

///|
fn encode_text(text : String) -> Bytes {
  @utf8.encode(text[:])
}

///|
fn valid_key(key : Bytes) -> Bool {
  if key.length() == 0 || key.length() > 4096 { return false }
  for byte in key {
    if byte.to_int() < 32 { return false }
  }
  true
}

///|
fn has_nul(data : Bytes) -> Bool {
  for byte in data { if byte == 0 { return true } }
  false
}

///|
fn composite3(first : Bytes, second : Bytes, third : Bytes) -> Bytes {
  let buffer = FixedArray::make(first.length() + second.length() + third.length() + 2, b'\x00')
  copy_bytes(buffer, 0, first)
  copy_bytes(buffer, first.length() + 1, second)
  copy_bytes(buffer, first.length() + second.length() + 2, third)
  Bytes::from_array(buffer[:])
}

///| Binary operations use explicit little-endian payloads, never MoonBit struct layouts.
pub fn pack_u64(value : UInt64) -> Bytes {
  let buffer = FixedArray::make(8, b'\x00')
  write_le(buffer, 0, value, 8)
  Bytes::from_array(buffer[:])
}

///|
pub fn pack_f32(value : Float) -> Bytes {
  let buffer = FixedArray::make(4, b'\x00')
  write_le(buffer, 0, value.reinterpret_as_uint().to_uint64(), 4)
  Bytes::from_array(buffer[:])
}
