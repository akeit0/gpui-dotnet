use std::{
    cell::{Cell, RefCell},
    collections::{HashMap, HashSet},
    rc::Rc,
    sync::Arc,
};

use gpui::{
    AnyElement, App, AppContext, Context, Entity, FocusHandle, IntoElement, Pixels, Point,
    ScrollHandle, SharedString, Subscription, WeakEntity, Window, point, px,
};

use crate::{
    abi::{ManagedCallbacks, NativeResourceCommand},
    app_host::ManagedView,
    collections::{
        CollectionEngine, CollectionRegistry, ListConfiguration, TableSpec,
        configuration::{last_callback, last_op_bits_f32, last_u32, shared, table_key},
    },
    dock::{DockConfiguration, ManagedDockResource, dock_configuration},
    extension::{
        NativeExtensionResourceKey, NativeExtensionStore, declaration as extension_declaration,
    },
    images::ManagedImageCache,
    input::{InputBindings, InputInitialState, InputPresentation, ManagedInput},
    semantic::{
        COMMAND_SCROLL_TO_BOTTOM, COMMAND_SCROLL_TO_LEFT, COMMAND_SCROLL_TO_OFFSET,
        COMMAND_SCROLL_TO_RIGHT, COMMAND_SCROLL_TO_TOP, NativeAdapter, OP_INPUT_CARET_RGBA,
        OP_INPUT_DISABLED, OP_INPUT_ON_CHANGED, OP_INPUT_ON_FOCUS_CHANGED, OP_INPUT_ON_SUBMITTED,
        OP_INPUT_ON_WRITE_COMPLETED, OP_INPUT_PASSWORD, OP_INPUT_PLACEHOLDER_RGBA,
        OP_INPUT_READ_ONLY, OP_INPUT_SELECTION_RGBA, OP_RESOURCE_OWNER, OP_SLIDER_AXIS,
        OP_SLIDER_DISABLED, OP_SLIDER_FILL_RGBA, OP_SLIDER_MAX, OP_SLIDER_MIN,
        OP_SLIDER_ON_CHANGED, OP_SLIDER_ON_RELEASED, OP_SLIDER_RANGE_END, OP_SLIDER_RANGE_START,
        OP_SLIDER_SCALE, OP_SLIDER_STEP, OP_SLIDER_THUMB_BORDER_RGBA, OP_SLIDER_THUMB_RGBA,
        OP_SLIDER_TRACK_RGBA, OP_SLIDER_VALUE, RESOURCE_DOCK, RESOURCE_FOCUS, RESOURCE_INPUT,
        RESOURCE_LIST, RESOURCE_SCROLL, RESOURCE_SLIDER, component_metadata,
    },
    slider::{ManagedSlider, SliderPresentation, SliderValue},
    snapshot::ValidatedSnapshot,
    theme::{NativeTheme, SharedTheme},
};

#[derive(Clone, Debug, Eq, Hash, PartialEq)]
pub(crate) struct ResourceKey {
    pub(crate) owner_view: u32,
    pub(crate) key: SharedString,
}

impl ResourceKey {
    pub(crate) fn new(owner_view: u32, key: SharedString) -> Self {
        Self { owner_view, key }
    }
}

#[derive(Clone, Debug)]
pub(crate) struct ResourceCommand {
    pub(crate) key: ResourceKey,
    pub(crate) resource_kind: u16,
    pub(crate) command: u16,
    pub(crate) a: u64,
    pub(crate) b: u64,
    pub(crate) data: SharedString,
}

impl ResourceCommand {
    pub(crate) fn from_abi(command: &NativeResourceCommand, key: &str, data: &str) -> Self {
        Self {
            key: ResourceKey::new(command.owner_view, SharedString::new(Arc::<str>::from(key))),
            resource_kind: command.resource_kind,
            command: command.command,
            a: command.a,
            b: command.b,
            data: SharedString::new(Arc::<str>::from(data)),
        }
    }
}

pub(crate) struct ResourceStore {
    pub(crate) shortcuts: crate::shortcuts::ShortcutDispatch,
    pub(crate) item_menus: Rc<crate::item_menu::ItemMenus>,
    pub(crate) item_tooltips: Rc<crate::item_tooltip::ItemTooltips>,
    session_id: u64,
    callbacks: ManagedCallbacks,
    theme: SharedTheme,
    scrolls: RefCell<HashMap<ResourceKey, Rc<ManagedScrollResource>>>,
    collections: CollectionRegistry,
    inputs: RefCell<HashMap<ResourceKey, Entity<ManagedInput>>>,
    focus_targets: RefCell<HashMap<ResourceKey, FocusHandle>>,
    sliders: RefCell<HashMap<ResourceKey, Entity<ManagedSlider>>>,
    docks: RefCell<HashMap<ResourceKey, Rc<RefCell<ManagedDockResource>>>>,
    dock_subscriptions: RefCell<HashMap<ResourceKey, Subscription>>,
    extensions: NativeExtensionStore,
    images: RefCell<Option<Entity<ManagedImageCache>>>,
    image_revision: Cell<u64>,
    pending: RefCell<HashMap<(u16, ResourceKey), Vec<ResourceCommand>>>,
    active_scratch: RefCell<HashSet<(u16, ResourceKey)>>,
    extension_active_scratch: RefCell<HashSet<NativeExtensionResourceKey>>,
}

impl ResourceStore {
    pub(crate) fn invalidate_artifacts(&self, keys: &mut [crate::abi::NativeArtifactKey]) -> bool {
        self.collections.invalidate_artifacts(keys)
    }

    pub(crate) fn publish_presence(
        &self,
        presence: &mut crate::presence::ResourcePresence,
        revision: u64,
    ) {
        presence.accept(
            &self.active_scratch.borrow(),
            &self.extension_active_scratch.borrow(),
            revision,
        );
    }

    pub(crate) fn new(session_id: u64, callbacks: ManagedCallbacks, theme: SharedTheme) -> Self {
        Self {
            item_menus: Rc::new(crate::item_menu::ItemMenus::default()),
            shortcuts: Default::default(),
            item_tooltips: Rc::new(crate::item_tooltip::ItemTooltips::default()),
            session_id,
            callbacks,
            theme,
            scrolls: RefCell::new(HashMap::new()),
            collections: CollectionRegistry::new(),
            inputs: RefCell::new(HashMap::new()),
            focus_targets: RefCell::new(HashMap::new()),
            sliders: RefCell::new(HashMap::new()),
            docks: RefCell::new(HashMap::new()),
            dock_subscriptions: RefCell::new(HashMap::new()),
            extensions: NativeExtensionStore::new(),
            images: RefCell::new(None),
            image_revision: Cell::new(0),
            pending: RefCell::new(HashMap::new()),
            active_scratch: RefCell::new(HashSet::new()),
            extension_active_scratch: RefCell::new(HashSet::new()),
        }
    }

    pub(crate) fn item_tooltip_target(
        &self,
        key: &ResourceKey,
        index: usize,
        target: u32,
        child: AnyElement,
    ) -> AnyElement {
        let Some(source) = self.collections.lookup(key) else {
            return child;
        };
        crate::item_tooltip::Target {
            child,
            source,
            index,
            target,
            tooltips: self.item_tooltips.clone(),
        }
        .into_any_element()
    }

    pub(crate) fn theme(&self) -> NativeTheme {
        *self.theme.borrow()
    }

    /// The view's scoped image cache, created on first render. Images resolve through this
    /// cache instead of GPUI's global asset map so navigation evicts what is no longer shown.
    pub(crate) fn image_cache(&self, cx: &mut App) -> Entity<ManagedImageCache> {
        if let Some(cache) = self.images.borrow().clone() {
            return cache;
        }
        let cache = ManagedImageCache::new(cx);
        *self.images.borrow_mut() = Some(cache.clone());
        cache
    }

    pub(crate) fn existing_image_cache(&self) -> Option<Entity<ManagedImageCache>> {
        self.images.borrow().clone()
    }

    /// Drops one image path from the view's cache, if present. Unknown paths are a no-op.
    /// Used by explicit managed eviction (changed files); the next paint reloads from disk.
    pub(crate) fn evict_image(&self, path: &str, window: &mut Window, cx: &mut App) {
        let Some(cache) = self.existing_image_cache() else {
            return;
        };
        let source = crate::images::image_resource(path);
        cache.update(cx, |cache, cx| {
            cache.evict_path(&source, window, cx);
        });
    }

    /// Forces the next render to reconcile image retention even when the snapshot revision
    /// is unchanged. Used after a budget change so a shrunken tier trims promptly.
    pub(crate) fn note_image_budget_changed(&self) {
        self.image_revision.set(u64::MAX);
    }

    /// Drops cached images the latest snapshot no longer declares: mounted nodes plus
    /// virtual-item batches. Runs at most once per snapshot revision; re-renders of the same
    /// revision describe the same live set.
    pub(crate) fn retain_live_images(
        &self,
        snapshot: &ValidatedSnapshot,
        revision: u64,
        window: &mut Window,
        cx: &mut App,
    ) {
        if self.image_revision.get() == revision {
            return;
        }
        self.image_revision.set(revision);
        let Some(cache) = self.existing_image_cache() else {
            return;
        };
        let mut live = crate::images::live_image_hashes(snapshot);
        self.collections.cached_image_hashes(&mut live);
        cache.update(cx, |cache, cx| cache.retain_live(&live, window, cx));
    }

    pub(crate) fn focus_target(
        &self,
        key: &ResourceKey,
        tab_stop: bool,
        window: &mut Window,
        cx: &mut Context<ManagedView>,
    ) -> FocusHandle {
        let focus = self
            .focus_targets
            .borrow_mut()
            .entry(key.clone())
            .or_insert_with(|| cx.focus_handle())
            .clone()
            .tab_stop(tab_stop);
        // Keep the current handle metadata as well as its stable identity.
        self.focus_targets
            .borrow_mut()
            .insert(key.clone(), focus.clone());
        let pending = self
            .pending
            .borrow_mut()
            .remove(&(RESOURCE_FOCUS, key.clone()));
        for command in pending.into_iter().flatten() {
            match command.command {
                crate::semantic::COMMAND_FOCUS_FOCUS => focus.focus(window, cx),
                crate::semantic::COMMAND_FOCUS_BLUR if focus.is_focused(window) => window.blur(cx),
                _ => {}
            }
        }
        focus
    }

    pub(crate) fn extensions(&self) -> &NativeExtensionStore {
        &self.extensions
    }

    pub(crate) fn scroll_resource(&self, key: &ResourceKey) -> Rc<ManagedScrollResource> {
        if let Some(resource) = self.scrolls.borrow().get(key) {
            return resource.clone();
        }

        let resource = Rc::new(ManagedScrollResource::new());
        self.scrolls
            .borrow_mut()
            .insert(key.clone(), resource.clone());
        self.apply_pending(RESOURCE_SCROLL, key);
        resource
    }

    pub(crate) fn list_resource(
        &self,
        key: &ResourceKey,
        configuration: &ListConfiguration,
        snapshot_revision: u64,
    ) -> Rc<RefCell<CollectionEngine>> {
        self.collections.declare(
            key,
            configuration,
            snapshot_revision,
            self.session_id,
            self.callbacks,
        )
    }

    /// Binds the column metadata declared by the current snapshot to a table's collection engine.
    /// A changed column table changes cell layout, so every cached item batch is invalidated.
    pub(crate) fn bind_table_spec(
        &self,
        key: &ResourceKey,
        spec: Rc<TableSpec>,
        row_engine: &Rc<RefCell<CollectionEngine>>,
    ) {
        self.collections.bind_table_spec(key, spec, row_engine)
    }

    pub(crate) fn table_spec(&self, key: &ResourceKey) -> Option<Rc<TableSpec>> {
        self.collections.table_spec(key)
    }

    /// Clones the collection-engine handles for diagnostics aggregation. Safe outside the render path
    /// (frame boundaries hold no RefCell borrows).
    pub(crate) fn list_engines(&self) -> Vec<Rc<RefCell<CollectionEngine>>> {
        self.collections.engines()
    }

    /// Discards retained managed item snapshots after an ambient theme or managed-code update.
    /// Tables use the same collection engines as lists, so this covers both.
    pub(crate) fn invalidate_managed_rendered_items(&self) {
        self.collections.invalidate_managed_rows()
    }

    pub(crate) fn list_engine_count(&self) -> usize {
        self.collections.engine_count()
    }

    pub(crate) fn input_resource(
        &self,
        configuration: &InputConfiguration,
        window: &mut Window,
        cx: &mut Context<ManagedView>,
    ) -> Entity<ManagedInput> {
        let existing = self.inputs.borrow().get(&configuration.key).cloned();
        let resource = if let Some(existing) = existing {
            existing
        } else {
            let created = cx.new(|cx| {
                ManagedInput::new(
                    self.session_id,
                    self.callbacks,
                    InputInitialState {
                        value: configuration.initial_value.as_ref(),
                        placeholder: configuration.placeholder.as_ref(),
                        disabled: configuration.disabled,
                        read_only: configuration.read_only,
                        password: configuration.password,
                        bindings: configuration.bindings,
                    },
                    self.theme.clone(),
                    cx,
                )
            });
            self.inputs
                .borrow_mut()
                .insert(configuration.key.clone(), created.clone());
            created
        };

        resource.update(cx, |input, cx| {
            input.configure(
                configuration.placeholder.as_ref(),
                configuration.disabled,
                configuration.read_only,
                configuration.password,
                configuration.bindings,
                configuration.presentation.clone(),
                cx,
            );
        });
        let pending = self
            .pending
            .borrow_mut()
            .remove(&(RESOURCE_INPUT, configuration.key.clone()))
            .unwrap_or_default();
        for command in pending {
            let completion = resource.update(cx, |input, cx| {
                input.apply_command_with_result(&command, window, cx)
            });
            if let Some(completion) = completion {
                // No managed callbacks while either the input or materializing root is borrowed.
                window.defer(cx, move |_, _| completion.emit());
            }
        }
        resource
    }

    pub(crate) fn slider_resource(
        &self,
        configuration: &SliderConfiguration,
        cx: &mut Context<ManagedView>,
    ) -> Entity<ManagedSlider> {
        let existing = self.sliders.borrow().get(&configuration.key).cloned();
        let resource = if let Some(existing) = existing {
            existing
        } else {
            let created = cx.new(|cx| {
                ManagedSlider::new(
                    self.session_id,
                    self.callbacks,
                    configuration,
                    self.theme.clone(),
                    cx,
                )
            });
            self.sliders
                .borrow_mut()
                .insert(configuration.key.clone(), created.clone());
            created
        };

        resource.update(cx, |slider, cx| slider.configure(configuration, cx));
        let pending = self
            .pending
            .borrow_mut()
            .remove(&(RESOURCE_SLIDER, configuration.key.clone()))
            .unwrap_or_default();
        for command in pending {
            resource.update(cx, |slider, cx| slider.apply_command(&command, cx));
        }
        resource
    }

    pub(crate) fn dock_resource(
        &self,
        configuration: &DockConfiguration,
        owner: WeakEntity<ManagedView>,
        window: &mut Window,
        cx: &mut Context<ManagedView>,
    ) -> Entity<gpui_base::dock::DockArea> {
        let resource = if let Some(existing) = self.docks.borrow().get(&configuration.key).cloned()
        {
            existing
                .borrow_mut()
                .configure(configuration, owner, window, cx);
            existing
        } else {
            let created = Rc::new(RefCell::new(ManagedDockResource::new(
                self.session_id,
                self.callbacks,
                configuration,
                owner,
                window,
                cx,
            )));
            self.docks
                .borrow_mut()
                .insert(configuration.key.clone(), created.clone());
            // Layout changes report through the area's event emitter, which
            // outlives any single render: subscribe once per retained area.
            // The handler reads the current layout token at fire time, since
            // render-bound bindings may be re-registered across snapshots.
            let events = created.borrow().events();
            let subscription = cx.subscribe_in(
                &created.borrow().area(),
                window,
                move |_: &mut ManagedView,
                      _: &Entity<gpui_base::dock::DockArea>,
                      event: &gpui_base::dock::DockEvent,
                      _: &mut Window,
                      _: &mut Context<ManagedView>| {
                    if matches!(event, gpui_base::dock::DockEvent::LayoutChanged) {
                        crate::dock::emit_dock_event(
                            &events,
                            crate::semantic::EVENT_DOCK_LAYOUT_CHANGED,
                            &[],
                        );
                    }
                },
            );
            self.dock_subscriptions
                .borrow_mut()
                .insert(configuration.key.clone(), subscription);
            created
        };

        // Controller commands queue until the resource exists; the
        // declaration wins ties by applying first.
        let pending = self
            .pending
            .borrow_mut()
            .remove(&(RESOURCE_DOCK, configuration.key.clone()))
            .unwrap_or_default();
        for command in pending {
            resource.borrow_mut().apply_command(&command, window, cx);
        }

        let area = resource.borrow().area();
        area
    }

    pub(crate) fn dispatch(&self, command: ResourceCommand) -> bool {
        if command.resource_kind == RESOURCE_LIST {
            self.collections.dispatch(command);
            return true;
        }
        let applied = match command.resource_kind {
            RESOURCE_SCROLL => self.apply_scroll_command(&command),
            RESOURCE_INPUT | RESOURCE_SLIDER | RESOURCE_DOCK | RESOURCE_FOCUS => false,
            _ => true,
        };
        if !applied {
            self.pending
                .borrow_mut()
                .entry((command.resource_kind, command.key.clone()))
                .or_default()
                .push(command);
        }
        applied
    }

    fn apply_pending(&self, resource_kind: u16, key: &ResourceKey) {
        let pending_key = (resource_kind, key.clone());
        let commands = self.pending.borrow_mut().remove(&pending_key);
        let Some(commands) = commands else {
            return;
        };
        for command in commands {
            let _ = match command.resource_kind {
                RESOURCE_SCROLL => self.apply_scroll_command(&command),
                _ => true,
            };
        }
    }

    fn apply_scroll_command(&self, command: &ResourceCommand) -> bool {
        let Some(resource) = self.scrolls.borrow().get(&command.key).cloned() else {
            return false;
        };
        let handle = &resource.handle;
        resource.interaction.remaining.set(Point::default());
        match command.command {
            COMMAND_SCROLL_TO_OFFSET => {
                let x = f32::from_bits(command.a as u32);
                let y = f32::from_bits(command.b as u32);
                if x.is_finite() && y.is_finite() {
                    handle.set_offset(point(px(-x), px(-y)));
                }
            }
            COMMAND_SCROLL_TO_TOP => handle.set_offset(point(px(0.), px(0.))),
            COMMAND_SCROLL_TO_BOTTOM => handle.scroll_to_bottom(),
            COMMAND_SCROLL_TO_LEFT => handle.set_offset(point(px(0.), handle.offset().y)),
            COMMAND_SCROLL_TO_RIGHT => {
                let max = handle.max_offset();
                handle.set_offset(point(-max.x, handle.offset().y))
            }
            _ => {}
        }
        true
    }

    pub(crate) fn retain_snapshot(&self, snapshot: &ValidatedSnapshot) {
        let mut active = self.active_scratch.borrow_mut();
        active.clear();
        let mut extension_active = self.extension_active_scratch.borrow_mut();
        extension_active.clear();
        for node in &snapshot.nodes {
            if let Some(key) = focus_target_key(snapshot, node) {
                active.insert((RESOURCE_FOCUS, key));
            }
            let Some(metadata) = component_metadata(node.component) else {
                continue;
            };
            if !matches!(
                metadata.adapter,
                NativeAdapter::Scroll
                    | NativeAdapter::List
                    | NativeAdapter::Table
                    | NativeAdapter::Input
                    | NativeAdapter::Slider
                    | NativeAdapter::DockArea
                    | NativeAdapter::NativeExtension
            ) {
                continue;
            }
            let active_resource = match metadata.adapter {
                NativeAdapter::Scroll => {
                    resource_key(snapshot, node).map(|key| (RESOURCE_SCROLL, key))
                }
                NativeAdapter::List => resource_key(snapshot, node).map(|key| (RESOURCE_LIST, key)),
                NativeAdapter::Table => table_key(snapshot, node).map(|key| (RESOURCE_LIST, key)),
                NativeAdapter::Input => input_configuration(snapshot, node)
                    .map(|configuration| (RESOURCE_INPUT, configuration.key)),
                NativeAdapter::Slider => slider_configuration(snapshot, node)
                    .map(|configuration| (RESOURCE_SLIDER, configuration.key)),
                NativeAdapter::DockArea => dock_configuration(snapshot, node)
                    .map(|configuration| (RESOURCE_DOCK, configuration.key)),
                NativeAdapter::NativeExtension => {
                    if let (Some(owner), Some(declaration)) = (
                        last_u32(snapshot, node, OP_RESOURCE_OWNER),
                        extension_declaration(node),
                    ) {
                        if owner != 0 {
                            extension_active.insert(declaration.resource_key(owner));
                        }
                    }
                    None
                }
                _ => None,
            };
            if let Some(resource) = active_resource {
                active.insert(resource);
            }
        }
        self.scrolls
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_SCROLL, key.clone())));
        self.collections.retain(&active);
        self.inputs
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_INPUT, key.clone())));
        self.focus_targets
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_FOCUS, key.clone())));
        self.sliders
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_SLIDER, key.clone())));
        self.docks
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_DOCK, key.clone())));
        self.dock_subscriptions
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_DOCK, key.clone())));
        self.extensions.retain(&extension_active);
        self.pending
            .borrow_mut()
            .retain(|(kind, key), _| active.contains(&(*kind, key.clone())));
    }
}

pub(crate) struct InputConfiguration {
    pub(crate) key: ResourceKey,
    pub(crate) initial_value: SharedString,
    pub(crate) placeholder: SharedString,
    pub(crate) disabled: bool,
    pub(crate) read_only: bool,
    pub(crate) password: bool,
    pub(crate) bindings: InputBindings,
    pub(crate) presentation: InputPresentation,
}

#[derive(Clone, Copy)]
pub(crate) struct SliderBindings {
    pub(crate) changed: u64,
    pub(crate) released: u64,
}

impl Default for SliderBindings {
    fn default() -> Self {
        Self {
            changed: 0,
            released: 0,
        }
    }
}

pub(crate) struct SliderConfiguration {
    pub(crate) key: ResourceKey,
    pub(crate) min: f32,
    pub(crate) max: f32,
    pub(crate) step: f32,
    pub(crate) initial_value: Option<SliderValue>,
    pub(crate) axis: gpui::Axis,
    pub(crate) disabled: bool,
    pub(crate) logarithmic: bool,
    pub(crate) bindings: SliderBindings,
    pub(crate) presentation: SliderPresentation,
}

#[derive(Default)]
pub(crate) struct ScrollInteraction {
    pub(crate) remaining: Cell<Point<Pixels>>,
    pub(crate) animating: Cell<bool>,
}

pub(crate) struct ManagedScrollResource {
    pub(crate) handle: ScrollHandle,
    pub(crate) interaction: Rc<ScrollInteraction>,
}

impl ManagedScrollResource {
    fn new() -> Self {
        Self {
            handle: ScrollHandle::new(),
            interaction: Rc::new(ScrollInteraction::default()),
        }
    }
}

pub(crate) fn resource_key(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
) -> Option<ResourceKey> {
    let owner = last_u32(snapshot, node, OP_RESOURCE_OWNER)?;
    if owner == 0 || node.data.is_empty() {
        return None;
    }
    Some(ResourceKey::new(owner, node.data.clone()))
}

pub(crate) fn focus_target_key(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
) -> Option<ResourceKey> {
    let key = snapshot.last_data_op(node, crate::semantic::OP_FOCUS_TARGET)?;
    Some(ResourceKey::new(
        last_u32(snapshot, node, OP_RESOURCE_OWNER)?,
        key,
    ))
}

pub(crate) fn input_configuration(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
) -> Option<InputConfiguration> {
    let owner = last_u32(snapshot, node, OP_RESOURCE_OWNER)?;
    if owner == 0 {
        return None;
    }

    let mut fields = node.data.split('\0');
    let key = fields.next()?;
    let initial_value = fields.next()?;
    let placeholder = fields.next()?;
    if key.is_empty() || fields.next().is_some() {
        return None;
    }

    Some(InputConfiguration {
        key: ResourceKey::new(owner, shared(key)),
        initial_value: shared(initial_value),
        placeholder: shared(placeholder),
        disabled: last_u32(snapshot, node, OP_INPUT_DISABLED).is_some_and(|value| value != 0),
        read_only: last_u32(snapshot, node, OP_INPUT_READ_ONLY).is_some_and(|value| value != 0),
        password: last_u32(snapshot, node, OP_INPUT_PASSWORD).is_some_and(|value| value != 0),
        presentation: InputPresentation {
            accessibility: crate::accessibility::Accessibility::from_snapshot(node, snapshot),
            placeholder: last_u32(snapshot, node, OP_INPUT_PLACEHOLDER_RGBA),
            caret: last_u32(snapshot, node, OP_INPUT_CARET_RGBA),
            selection: last_u32(snapshot, node, OP_INPUT_SELECTION_RGBA),
        },
        bindings: InputBindings {
            changed: last_callback(snapshot, node, OP_INPUT_ON_CHANGED),
            submitted: last_callback(snapshot, node, OP_INPUT_ON_SUBMITTED),
            focus_changed: last_callback(snapshot, node, OP_INPUT_ON_FOCUS_CHANGED),
            write_completed: last_callback(snapshot, node, OP_INPUT_ON_WRITE_COMPLETED),
        },
    })
}

pub(crate) fn slider_configuration(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
) -> Option<SliderConfiguration> {
    let key = resource_key(snapshot, node)?;
    let min = last_op_bits_f32(snapshot, node, OP_SLIDER_MIN).unwrap_or(0.0);
    let max = last_op_bits_f32(snapshot, node, OP_SLIDER_MAX).unwrap_or(100.0);
    let step = last_op_bits_f32(snapshot, node, OP_SLIDER_STEP).unwrap_or(1.0);
    if !min.is_finite() || !max.is_finite() || min >= max || !step.is_finite() || step <= 0.0 {
        return None;
    }

    let axis = match last_u32(snapshot, node, OP_SLIDER_AXIS).unwrap_or(0) {
        0 => gpui::Axis::Horizontal,
        1 => gpui::Axis::Vertical,
        _ => return None,
    };
    let logarithmic = match last_u32(snapshot, node, OP_SLIDER_SCALE).unwrap_or(0) {
        0 => false,
        1 if min > 0.0 => true,
        _ => return None,
    };
    let single = last_op_bits_f32(snapshot, node, OP_SLIDER_VALUE);
    let range_start = last_op_bits_f32(snapshot, node, OP_SLIDER_RANGE_START);
    let range_end = last_op_bits_f32(snapshot, node, OP_SLIDER_RANGE_END);
    let initial_value = match (single, range_start, range_end) {
        (Some(value), None, None) => Some(SliderValue::Single(value)),
        (None, Some(start), Some(end)) if start <= end => Some(SliderValue::Range(start, end)),
        (None, None, None) => None,
        _ => return None,
    };

    Some(SliderConfiguration {
        key,
        min,
        max,
        step,
        initial_value,
        axis,
        disabled: last_u32(snapshot, node, OP_SLIDER_DISABLED).unwrap_or(0) != 0,
        logarithmic,
        presentation: SliderPresentation {
            accessibility: crate::accessibility::Accessibility::from_snapshot(node, snapshot),
            track: last_u32(snapshot, node, OP_SLIDER_TRACK_RGBA),
            fill: last_u32(snapshot, node, OP_SLIDER_FILL_RGBA),
            thumb: last_u32(snapshot, node, OP_SLIDER_THUMB_RGBA),
            thumb_border: last_u32(snapshot, node, OP_SLIDER_THUMB_BORDER_RGBA),
        },
        bindings: SliderBindings {
            changed: last_callback(snapshot, node, OP_SLIDER_ON_CHANGED),
            released: last_callback(snapshot, node, OP_SLIDER_ON_RELEASED),
        },
    })
}
