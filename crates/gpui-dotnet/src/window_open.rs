use crate::abi::NativeApplicationCommand;

pub(crate) const COMMAND: u16 = 16;
const VERSION: u32 = 1;
const HEADER_SIZE: usize = 20;
const MAX_TITLE_BYTES: usize = 4096;

pub(crate) struct WindowOpenPayload {
    pub(crate) title: String,
    pub(crate) minimum_width: f32,
    pub(crate) minimum_height: f32,
}

pub(crate) fn parse(command: &NativeApplicationCommand) -> Result<WindowOpenPayload, i32> {
    if command.command != COMMAND
        || command.reserved != 0
        || command.reserved2 != 0
        || command.title_length < (HEADER_SIZE + 1) as i32
        || command.title_length as usize > HEADER_SIZE + MAX_TITLE_BYTES
        || command.title.is_null()
    {
        return Err(-62);
    }
    let bytes = unsafe { crate::pointer::slice(command.title, command.title_length as usize) };
    let field = |at: usize| u32::from_le_bytes(bytes[at..at + 4].try_into().unwrap());
    let version = field(0);
    let minimum_width = f32::from_bits(field(4));
    let minimum_height = f32::from_bits(field(8));
    let title_length = field(12) as usize;
    let reserved = field(16);
    if version != VERSION
        || reserved != 0
        || !(1..=MAX_TITLE_BYTES).contains(&title_length)
        || HEADER_SIZE + title_length != bytes.len()
        || !minimum_width.is_finite()
        || !minimum_height.is_finite()
        || minimum_width <= 0.0
        || minimum_height <= 0.0
        || minimum_width > command.width
        || minimum_height > command.height
    {
        return Err(-62);
    }
    let title = std::str::from_utf8(&bytes[HEADER_SIZE..]).map_err(|_| -63)?;
    Ok(WindowOpenPayload {
        title: title.to_owned(),
        minimum_width,
        minimum_height,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn payload() -> Vec<u8> {
        let mut bytes = Vec::new();
        for field in [VERSION, 480f32.to_bits(), 320f32.to_bits(), 5, 0] {
            bytes.extend_from_slice(&field.to_le_bytes());
        }
        bytes.extend_from_slice(b"Title");
        bytes
    }

    fn command(bytes: &[u8]) -> NativeApplicationCommand {
        NativeApplicationCommand {
            window_id: 1,
            command: COMMAND,
            flags: 0,
            reserved: 0,
            title: bytes.as_ptr(),
            title_length: bytes.len() as i32,
            reserved2: 0,
            left: 0.0,
            top: 0.0,
            width: 800.0,
            height: 600.0,
        }
    }

    #[test]
    fn parses_title_and_minimum_size() {
        let payload = payload();
        let open = parse(&command(&payload)).unwrap();
        assert_eq!(open.title, "Title");
        assert_eq!(open.minimum_width, 480.0);
        assert_eq!(open.minimum_height, 320.0);
    }

    #[test]
    fn rejects_malformed_lengths_reserved_and_excessive_minimum() {
        let mut payload = payload();
        payload.pop();
        assert!(parse(&command(&payload)).is_err());
        payload.push(b'e');
        payload[16] = 1;
        assert!(parse(&command(&payload)).is_err());
        payload[16] = 0;
        let mut command = command(&payload);
        command.width = 400.0;
        assert!(parse(&command).is_err());
    }
}
