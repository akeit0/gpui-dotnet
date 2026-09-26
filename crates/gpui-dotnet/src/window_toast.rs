use std::time::{Duration, Instant};

use gpui::{
    Anchor, AnyElement, ClickEvent, Context, FocusHandle, IntoElement, SharedString, deferred, div,
    prelude::*, px, rgba,
};
use gpui_base::{Toast, ToastManager, ToastMotion, ToastOptions, ToastStack, ToastStackState};

use crate::{abi::NativeApplicationCommand, app_host::ManagedView, theme::NativeTheme};

pub(crate) const POST_COMMAND: u16 = 13;
pub(crate) const DISMISS_COMMAND: u16 = 14;
pub(crate) const CLEAR_COMMAND: u16 = 15;
const PAYLOAD_VERSION: u32 = 1;
const HEADER_SIZE: usize = 24;
const MAX_ID: usize = 256;
const MAX_TITLE: usize = 4096;
const MAX_DESCRIPTION: usize = 16384;
const MAX_TIMEOUT_MS: u32 = 86_400_000;
const MAX_PAYLOAD: usize = HEADER_SIZE + MAX_ID + MAX_TITLE + MAX_DESCRIPTION;
const VISIBLE_LIMIT: usize = 3;
const PRIORITY: usize = 400;

#[derive(Clone, Debug)]
pub(crate) struct WindowToast {
    pub(crate) id: SharedString,
    pub(crate) title: SharedString,
    pub(crate) description: Option<SharedString>,
    pub(crate) timeout: Option<Duration>,
}

pub(crate) fn parse_post(command: &NativeApplicationCommand) -> Result<WindowToast, i32> {
    if command.command != POST_COMMAND
        || command.flags != 0
        || command.left != 0.0
        || command.top != 0.0
        || command.width != 0.0
        || command.height != 0.0
        || command.title_length < HEADER_SIZE as i32
        || command.title_length as usize > MAX_PAYLOAD
        || command.title.is_null()
    {
        return Err(-62);
    }
    let bytes = unsafe { crate::pointer::slice(command.title, command.title_length as usize) };
    let field = |at: usize| u32::from_le_bytes(bytes[at..at + 4].try_into().unwrap());
    let version = field(0);
    let timeout_ms = field(4);
    let id_len = field(8) as usize;
    let title_len = field(12) as usize;
    let description_len = field(16) as usize;
    let reserved = field(20);
    if version != PAYLOAD_VERSION
        || reserved != 0
        || timeout_ms > MAX_TIMEOUT_MS
        || !(1..=MAX_ID).contains(&id_len)
        || !(1..=MAX_TITLE).contains(&title_len)
        || description_len > MAX_DESCRIPTION
        || HEADER_SIZE + id_len + title_len + description_len != bytes.len()
    {
        return Err(-62);
    }
    let id_end = HEADER_SIZE + id_len;
    let title_end = id_end + title_len;
    let id = std::str::from_utf8(&bytes[HEADER_SIZE..id_end]).map_err(|_| -63)?;
    let title = std::str::from_utf8(&bytes[id_end..title_end]).map_err(|_| -63)?;
    let description = std::str::from_utf8(&bytes[title_end..]).map_err(|_| -63)?;
    Ok(WindowToast {
        id: id.to_owned().into(),
        title: title.to_owned().into(),
        description: (!description.is_empty()).then(|| description.to_owned().into()),
        timeout: (timeout_ms != 0).then(|| Duration::from_millis(timeout_ms.into())),
    })
}

pub(crate) fn valid_dismiss_id(id: &str) -> bool {
    (1..=MAX_ID).contains(&id.len())
}

pub(crate) struct WindowToastHost {
    manager: ToastManager<SharedString, WindowToast>,
    state: ToastStackState,
    focus: Option<FocusHandle>,
}

impl WindowToastHost {
    pub(crate) fn new() -> Self {
        Self {
            manager: ToastManager::new(ToastMotion::sonner()),
            state: ToastStackState::default(),
            focus: None,
        }
    }

    pub(crate) fn push(&mut self, toast: WindowToast, now: Instant) {
        let id = toast.id.clone();
        let options = ToastOptions {
            timeout: toast.timeout,
        };
        self.manager.push(id, toast, options, now);
    }

    pub(crate) fn dismiss(&mut self, id: &str, now: Instant) -> bool {
        self.manager
            .dismiss(&SharedString::from(id.to_owned()), now)
    }

    pub(crate) fn clear(&mut self, now: Instant) -> bool {
        !self.manager.dismiss_all(now).is_empty()
    }

    pub(crate) fn advance(&mut self, now: Instant) -> bool {
        let changed = self.manager.advance(now, self.state.is_expanded()).changed;
        if self.manager.is_empty() {
            self.state = ToastStackState::default();
            self.focus = None;
        }
        changed
    }

    pub(crate) fn is_empty(&self) -> bool {
        self.manager.is_empty()
    }

    pub(crate) fn layer(
        &mut self,
        theme: NativeTheme,
        cx: &mut Context<ManagedView>,
    ) -> AnyElement {
        let focus = self
            .focus
            .get_or_insert_with(|| cx.focus_handle().tab_stop(true))
            .clone();
        let items = self
            .manager
            .visible(VISIBLE_LIMIT)
            .map(|(id, toast, status)| (id.clone(), toast.clone(), status))
            .collect::<Vec<_>>();
        let stack = items.into_iter().fold(
            ToastStack::new("gpui-dotnet-toasts", self.state.clone()),
            |stack, (id, toast, status)| {
                let dismiss_id = id.clone();
                stack.item(
                    id.clone(),
                    Toast::new(id)
                        .transition_status(status)
                        .occlude()
                        .on_click(cx.listener(move |view, _: &ClickEvent, _, cx| {
                            view.dismiss_toast(&dismiss_id, cx);
                        }))
                        .flex()
                        .flex_col()
                        .gap(px(4.))
                        .p(px(12.))
                        .rounded(px(8.))
                        .bg(rgba(theme.surface_background))
                        .text_color(rgba(theme.text))
                        .border_1()
                        .border_color(rgba(theme.border))
                        .child(toast.title)
                        .when_some(toast.description, |this, description| {
                            this.child(div().text_color(rgba(theme.text_muted)).child(description))
                        }),
                )
            },
        );
        deferred(
            stack
                .placement(Anchor::TopRight)
                .focus_handle(focus)
                .absolute()
                .top(px(16.))
                .right(px(16.))
                .w(px(320.)),
        )
        .with_priority(PRIORITY)
        .into_any_element()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn command(payload: &[u8]) -> NativeApplicationCommand {
        NativeApplicationCommand {
            window_id: 1,
            command: POST_COMMAND,
            flags: 0,
            reserved: 0,
            title: payload.as_ptr(),
            title_length: payload.len() as i32,
            reserved2: 0,
            left: 0.0,
            top: 0.0,
            width: 0.0,
            height: 0.0,
        }
    }

    fn payload() -> Vec<u8> {
        let mut bytes = Vec::new();
        for value in [1u32, 5000, 2, 5, 4, 0] {
            bytes.extend_from_slice(&value.to_le_bytes());
        }
        bytes.extend_from_slice(b"idtitlebody");
        bytes
    }

    #[test]
    fn payload_parses_bounded_utf8_and_timeout() {
        let payload = payload();
        let toast = parse_post(&command(&payload)).unwrap();
        assert_eq!(toast.id.as_ref(), "id");
        assert_eq!(toast.title.as_ref(), "title");
        assert_eq!(toast.description.as_deref(), Some("body"));
        assert_eq!(toast.timeout, Some(Duration::from_secs(5)));
    }

    #[test]
    fn payload_rejects_truncation_reserved_and_invalid_utf8() {
        let mut payload = payload();
        payload.pop();
        assert!(parse_post(&command(&payload)).is_err());
        payload.push(b'y');
        payload[20] = 1;
        assert!(parse_post(&command(&payload)).is_err());
        payload[20] = 0;
        payload[24] = 0xff;
        assert_eq!(parse_post(&command(&payload)).err(), Some(-63));
    }

    #[test]
    fn stable_id_replaces_and_dismisses_one_toast() {
        let now = Instant::now();
        let mut host = WindowToastHost::new();
        let first = WindowToast {
            id: "save".into(),
            title: "Saved".into(),
            description: None,
            timeout: None,
        };
        host.push(first.clone(), now);
        host.push(
            WindowToast {
                title: "Updated".into(),
                ..first
            },
            now,
        );
        assert_eq!(host.manager.len(), 1);
        assert_eq!(
            host.manager
                .get(&SharedString::from("save"))
                .unwrap()
                .title
                .as_ref(),
            "Updated"
        );
        assert!(host.dismiss("save", now));
        assert!(!host.dismiss("save", now));
        assert!(host.advance(now + Duration::from_secs(1)));
        assert!(host.is_empty());
    }
}
