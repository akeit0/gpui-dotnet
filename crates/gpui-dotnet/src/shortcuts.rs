use gpui::KeyDownEvent;
use std::cell::Cell;

/// Dispatch-local state shared only by shortcut listeners. An isolated descendant suppresses
/// ancestor shortcuts without stopping GPUI propagation (including Tab traversal and observers).
#[derive(Default)]
pub(crate) struct ShortcutDispatch {
    blocked: Cell<bool>,
}

#[derive(Clone, Copy)]
pub(crate) struct Binding {
    pub(crate) token: u64,
    pub(crate) descriptor: u64,
}

#[derive(Default)]
pub(crate) struct Match {
    pub(crate) token: Option<u64>,
    pub(crate) consume: bool,
}

impl ShortcutDispatch {
    pub(crate) fn begin(&self) {
        self.blocked.set(false);
    }

    pub(crate) fn resolve(
        &self,
        bindings: &[Binding],
        isolated: bool,
        event: &KeyDownEvent,
    ) -> Match {
        if self.blocked.replace(self.blocked.get() || isolated) || event.prefer_character_input {
            return Match::default();
        }
        let modifiers = &event.keystroke.modifiers;
        let flags = u32::from(modifiers.control)
            | (u32::from(modifiers.alt) << 1)
            | (u32::from(modifiers.shift) << 2)
            | (u32::from(modifiers.platform) << 3)
            | (u32::from(modifiers.function) << 4);
        let Some(key) = key_code(&event.keystroke.key) else {
            return Match::default();
        };
        let Some(binding) = bindings.iter().rev().find(|binding| {
            binding.descriptor as u16 == key
                && resolved_modifiers(
                    ((binding.descriptor >> 16) & 63) as u32,
                    cfg!(target_os = "macos"),
                ) == flags
        }) else {
            return Match::default();
        };
        let options = binding.descriptor >> 24;
        let enabled = options & 2 == 0;
        let repeated = event.is_held && options & 4 == 0;
        // Disabled or ignored repeated commands still shadow an ancestor's matching command.
        if !enabled || repeated {
            self.blocked.set(true);
        }
        Match {
            token: (enabled && !repeated).then_some(binding.token),
            consume: options & 1 == 0,
        }
    }
}

fn resolved_modifiers(flags: u32, macos: bool) -> u32 {
    if flags & 32 == 0 {
        flags
    } else {
        (flags & !32) | if macos { 8 } else { 1 }
    }
}

fn key_code(key: &str) -> Option<u16> {
    const NAMES: &[&str] = &[
        "a",
        "b",
        "c",
        "d",
        "e",
        "f",
        "g",
        "h",
        "i",
        "j",
        "k",
        "l",
        "m",
        "n",
        "o",
        "p",
        "q",
        "r",
        "s",
        "t",
        "u",
        "v",
        "w",
        "x",
        "y",
        "z",
        "0",
        "1",
        "2",
        "3",
        "4",
        "5",
        "6",
        "7",
        "8",
        "9",
        "escape",
        "enter",
        "tab",
        "backspace",
        "delete",
        "space",
        "left",
        "right",
        "up",
        "down",
        "home",
        "end",
        "pageup",
        "pagedown",
        "insert",
        "f1",
        "f2",
        "f3",
        "f4",
        "f5",
        "f6",
        "f7",
        "f8",
        "f9",
        "f10",
        "f11",
        "f12",
        "f13",
        "f14",
        "f15",
        "f16",
        "f17",
        "f18",
        "f19",
        "f20",
        "f21",
        "f22",
        "f23",
        "f24",
        "-",
        "=",
        "[",
        "]",
        "\\",
        ";",
        "'",
        ",",
        ".",
        "/",
        "`",
    ];
    NAMES
        .iter()
        .position(|name| name.eq_ignore_ascii_case(key))
        .map(|index| (index + 1) as u16)
}

#[cfg(test)]
mod tests {
    use super::*;
    fn event(held: bool) -> KeyDownEvent {
        KeyDownEvent {
            keystroke: gpui::Keystroke::parse("ctrl-s").unwrap(),
            is_held: held,
            prefer_character_input: false,
        }
    }
    #[test]
    fn shortcut_repeat_consumption_shadowing_and_primary_mapping() {
        assert_eq!(resolved_modifiers(32 | 4, false), 1 | 4);
        assert_eq!(resolved_modifiers(32 | 4, true), 8 | 4);
        let dispatch = ShortcutDispatch::default();
        let descriptor = 19 | (1 << 16);
        let parent = [Binding {
            token: 1,
            descriptor,
        }];
        for (flags, held, token, consume) in [
            (0, false, Some(3), true),
            (0, true, None, true),
            (4, true, Some(3), true),
            (2, false, None, true),
            (1, false, Some(3), false),
            (3, false, None, false),
        ] {
            dispatch.begin();
            let bindings = [
                Binding {
                    token: 2,
                    descriptor,
                },
                Binding {
                    token: 3,
                    descriptor: descriptor | (flags << 24),
                },
            ];
            let result = dispatch.resolve(&bindings, false, &event(held));
            assert_eq!(result.token, token);
            assert_eq!(result.consume, consume);
            if token.is_none() {
                assert_eq!(dispatch.resolve(&parent, false, &event(false)).token, None);
            }
        }
        dispatch.begin();
        assert_eq!(
            dispatch.resolve(&parent, false, &event(false)).token,
            Some(1)
        );
        dispatch.begin();
        let mut text = event(false);
        text.prefer_character_input = true;
        let result = dispatch.resolve(&parent, false, &text);
        assert_eq!(result.token, None);
        assert!(!result.consume);
        for bad in [0, 87, 1 << 27, 1 | (64 << 16), 1 | (33 << 16), 1, 42, 76] {
            assert_eq!(
                crate::semantic::payload_error(crate::semantic::OP_ON_SHORTCUT, 1, bad),
                -66
            );
        }
    }
}
