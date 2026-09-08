use std::cell::Cell;

/// Foreground-only keyboard position, independent of item-batch cache lifetime and selection.
pub(crate) struct CollectionCursor {
    index: Cell<usize>,
    count: Cell<usize>,
    epoch: Cell<u64>,
}

impl CollectionCursor {
    pub(crate) fn new(count: usize) -> Self {
        Self {
            index: Cell::new(0),
            count: Cell::new(count),
            epoch: Cell::new(1),
        }
    }

    pub(crate) fn active(&self) -> Option<usize> {
        (self.count.get() != 0).then(|| self.index.get())
    }

    pub(crate) fn set(&self, index: usize) -> bool {
        if index >= self.count.get() {
            return false;
        }
        self.index.set(index);
        true
    }

    pub(crate) fn epoch(&self) -> u64 {
        self.epoch.get()
    }

    pub(crate) fn set_from_item(&self, index: usize, epoch: u64) -> bool {
        self.epoch.get() == epoch && self.set(index)
    }

    pub(crate) fn invalidate_items(&self) {
        self.epoch.set(
            self.epoch
                .get()
                .checked_add(1)
                .expect("collection cursor epoch exhausted"),
        );
    }

    pub(crate) fn reset(&self, count: usize) {
        self.index.set(0);
        self.count.set(count);
        self.invalidate_items();
    }

    pub(crate) fn splice(&self, start: usize, removed: usize, inserted: usize) {
        let count = self.count.get() - removed + inserted;
        let index = self.index.get();
        let next = if self.count.get() == 0 {
            0
        } else if index < start {
            index
        } else if index >= start + removed {
            index - removed + inserted
        } else {
            // The active item was removed: choose its replacement/successor, or the final item.
            start
        };
        self.index.set(next.min(count.saturating_sub(1)));
        self.count.set(count);
        if removed != 0 || inserted != 0 {
            self.invalidate_items();
        }
    }
}
