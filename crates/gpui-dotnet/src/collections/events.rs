use crate::{
    abi::{ManagedCallbacks, NativeControlEvent},
    semantic::{EVENT_LIST_ACTIVATED, EVENT_LIST_SELECTION_REQUESTED},
};

#[derive(Clone, Copy)]
pub(crate) enum ListItemEventKind {
    Activation,
    Selection,
}

#[derive(Clone, Copy)]
pub(crate) struct ListItemEvents {
    pub(crate) session_id: u64,
    pub(crate) callbacks: ManagedCallbacks,
    pub(crate) activation_token: u64,
    pub(crate) selection_token: u64,
    pub(crate) index: u32,
    pub(crate) item_id: Option<u64>,
    pub(crate) content_revision: Option<u64>,
}

impl ListItemEvents {
    pub(crate) fn emit(self, kind: ListItemEventKind, keyboard: bool) -> i32 {
        let (kind, token) = match kind {
            ListItemEventKind::Activation => (EVENT_LIST_ACTIVATED, self.activation_token),
            ListItemEventKind::Selection => (EVENT_LIST_SELECTION_REQUESTED, self.selection_token),
        };
        if token == 0 {
            return 0;
        }
        let mut data = [0u8; 16];
        data[..4].copy_from_slice(&self.index.to_le_bytes());
        data[8..].copy_from_slice(&self.item_id.unwrap_or(0).to_le_bytes());
        let event = NativeControlEvent {
            kind,
            flags: u16::from(keyboard) | (u16::from(self.content_revision.is_some()) << 1),
            revision: self.content_revision.unwrap_or(0),
            data: data.as_ptr(),
            data_length: 16,
            reserved: 0,
            reserved2: 0,
        };
        let callback = self
            .callbacks
            .control_event
            .expect("callbacks validated at startup");
        unsafe { callback(self.session_id, token, &event) }
    }
}
