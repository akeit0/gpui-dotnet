use std::{
    borrow::Cow,
    cell::{Cell, RefCell},
    collections::HashSet,
    path::{Component as PathComponent, Path},
    rc::Rc,
    sync::Once,
    time::Duration,
};

use gpui::{
    AnyElement, App, AppContext as _, AssetSource, Axis, Entity, FocusHandle, Hsla,
    InteractiveElement as _, IntoElement as _, KeyDownEvent, Keystroke, ParentElement as _, Role,
    SharedString, Styled as _, Subscription, Window, div, px, rgba,
};
use gpui_component::{
    ActiveTheme as _, Disableable as _, Icon, Selectable as _, Sizable as _,
    alert::{Alert, AlertVariant as NativeAlertVariant},
    attachment::{
        Attachment, AttachmentActions, AttachmentContent, AttachmentDescription, AttachmentMedia,
        AttachmentStatus as NativeAttachmentStatus, AttachmentTitle,
    },
    avatar::Avatar,
    badge::Badge,
    breadcrumb::{Breadcrumb, BreadcrumbItem},
    bubble::{
        Bubble, BubbleGroup, BubbleReactionSide as NativeBubbleReactionSide, BubbleReactions,
        BubbleVariant as NativeBubbleVariant,
    },
    button::{
        Button, ButtonVariant as NativeButtonVariant, ButtonVariants as _, Toggle,
        ToggleVariant as NativeToggleVariant, ToggleVariants as _,
    },
    checkbox::Checkbox,
    collapsible::Collapsible,
    description_list::{DescriptionItem, DescriptionList},
    empty::{
        Empty as ComponentEmpty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia,
        EmptyMediaVariant as NativeEmptyMediaVariant, EmptyTitle,
    },
    form::{Field, Form},
    group_box::{GroupBox, GroupBoxVariant as NativeGroupBoxVariant, GroupBoxVariants as _},
    input::{InputEvent, Rope, Textarea, TextareaState},
    kbd::Kbd,
    label::{HighlightsMatch, Label},
    link::Link,
    marker::{
        Marker, MarkerAlignment as NativeMarkerAlignment, MarkerContent, MarkerIcon,
        MarkerLoadingStyle as NativeMarkerLoadingStyle, MarkerVariant as NativeMarkerVariant,
    },
    message::{
        Message, MessageAlignment as NativeMessageAlignment, MessageAvatar, MessageContent,
        MessageFooter, MessageGroup, MessageHeader,
    },
    pagination::Pagination,
    progress::{Progress, ProgressCircle},
    radio::Radio,
    rating::Rating,
    separator::Separator,
    shimmer::ShimmerText,
    skeleton::Skeleton,
    spinner::Spinner,
    status_bar::StatusBar,
    switch::Switch,
    tab::{Tab, TabBar, TabVariant as NativeTabVariant},
    tag::{Tag, TagVariant as NativeTagVariant},
    toolbar::{Toolbar, ToolbarGroup},
    try_parse_color,
};
use gpui_dotnet::{
    abi::GpuiDotnetApiV3,
    extension::{
        NativeExtension, NativeExtensionCommand, NativeExtensionDescriptor,
        NativeExtensionEventEmitter, NativeExtensionRequest, NativeExtensionStore, ResolvedTheme,
        install_native_extensions,
    },
};
use gpui_dotnet_editor_provider::EDITOR_EXTENSION;

mod accordion;
#[path = "component_schema.g.rs"]
mod component_schema;
mod dates;
mod selection;
mod tree;

use component_schema::*;

struct ComponentsExtension;
struct ComponentsAssets;

static COMPONENTS_ASSETS: ComponentsAssets = ComponentsAssets;

impl AssetSource for ComponentsAssets {
    fn load(&self, path: &str) -> gpui::Result<Option<Cow<'static, [u8]>>> {
        if let Some(relative) = path.strip_prefix("app-assets/") {
            if !valid_app_asset_path(relative) {
                return Err(std::io::Error::new(
                    std::io::ErrorKind::InvalidInput,
                    "Invalid application asset path.",
                )
                .into());
            }
            let executable = std::env::current_exe()?;
            let directory = executable
                .parent()
                .ok_or_else(|| std::io::Error::other("Executable has no parent directory."))?;
            return Ok(Some(Cow::Owned(std::fs::read(directory.join(relative))?)));
        }
        gpui_kit_assets::Assets.load(path)
    }

    fn list(&self, path: &str) -> gpui::Result<Vec<SharedString>> {
        gpui_kit_assets::Assets.list(path)
    }
}

fn valid_app_asset_path(relative: &str) -> bool {
    !relative.is_empty()
        && !relative.contains('\\')
        && Path::new(relative)
            .components()
            .all(|component| matches!(component, PathComponent::Normal(_)))
}

impl NativeExtension for ComponentsExtension {
    fn descriptor(&self) -> NativeExtensionDescriptor {
        NativeExtensionDescriptor {
            id: EXTENSION_ID,
            version: SCHEMA_VERSION,
            schema_hash: SCHEMA_HASH,
        }
    }

    fn asset_source(&self) -> Option<&'static dyn gpui::AssetSource> {
        Some(&COMPONENTS_ASSETS)
    }

    fn initialize(&self, cx: &mut App) {
        gpui_component::init(cx);
    }

    fn apply_theme(&self, cx: &mut App) {
        let Some(theme) = cx.try_global::<ResolvedTheme>().cloned() else {
            return;
        };
        project_theme(theme, cx);
    }

    fn validate_command(&self, command: &NativeExtensionCommand) -> bool {
        if command.resource_key.extension_id() != EXTENSION_ID
            || command.resource_key.component_kind() != COMPONENT_TEXTAREA
            || command.flags != 0
            || command.expected_revision != 0
        {
            return false;
        }
        match command.command {
            TEXTAREA_COMMAND_FOCUS => command.payload.is_empty(),
            TEXTAREA_COMMAND_SET_VALUE => {
                std::str::from_utf8(&command.payload).is_ok_and(|value| !value.contains('\0'))
            }
            _ => false,
        }
    }

    fn materialize(
        &self,
        request: NativeExtensionRequest,
        resources: &NativeExtensionStore,
        window: &mut Window,
        cx: &mut App,
    ) -> Result<AnyElement, SharedString> {
        if request.resource_key.component_kind() != COMPONENT_TEXTAREA
            && !request.commands.is_empty()
        {
            return Err("The component catalog does not define imperative commands.".into());
        }
        match request.resource_key.component_kind() {
            COMPONENT_ATTACHMENT => attachment(request),
            COMPONENT_TEXTAREA => textarea(request, resources, window, cx),
            COMPONENT_SELECT => selection::select(request, resources, window, cx),
            COMPONENT_COMBOBOX => selection::combobox(request, resources, window, cx),
            COMPONENT_TREE => tree::tree(request, resources, window, cx),
            COMPONENT_ACCORDION => accordion::accordion(request, resources, window, cx),
            COMPONENT_CALENDAR => dates::calendar(request, resources, window, cx),
            COMPONENT_DATE_PICKER => dates::date_picker(request, resources, window, cx),
            COMPONENT_FORM => form(request),
            COMPONENT_EMPTY => empty(request),
            COMPONENT_TOOLBAR => toolbar(request),
            COMPONENT_TOOLBAR_GROUP => toolbar_group(request),
            COMPONENT_STATUS_BAR => status_bar(request),
            COMPONENT_BUBBLE => bubble(request),
            COMPONENT_BUBBLE_GROUP => bubble_group(request),
            COMPONENT_MESSAGE => message(request),
            COMPONENT_MESSAGE_GROUP => message_group(request),
            COMPONENT_MARKER => marker(request),
            COMPONENT_ICON => icon(request),
            COMPONENT_DESCRIPTION_LIST => description_list(request),
            COMPONENT_BREADCRUMB => breadcrumb(request),
            COMPONENT_TABS => tabs(request, resources, cx),
            COMPONENT_SPINNER => spinner(request),
            COMPONENT_SKELETON => skeleton(request),
            COMPONENT_SEPARATOR => separator(request),
            COMPONENT_BADGE => badge(request),
            COMPONENT_TAG => tag(request),
            COMPONENT_PROGRESS => progress(request),
            COMPONENT_PROGRESS_CIRCLE => progress_circle(request),
            COMPONENT_RATING => rating(request),
            COMPONENT_BUTTON => button(request),
            COMPONENT_ALERT => alert(request),
            COMPONENT_GROUP_BOX => group_box(request),
            COMPONENT_LABEL => label(request),
            COMPONENT_KBD => kbd(request),
            COMPONENT_LINK => link(request),
            COMPONENT_AVATAR => avatar(request),
            COMPONENT_SHIMMER_TEXT => shimmer_text(request),
            COMPONENT_SWITCH => switch(request),
            COMPONENT_CHECKBOX => checkbox(request),
            COMPONENT_RADIO => radio(request),
            COMPONENT_TOGGLE => toggle(request),
            COMPONENT_PAGINATION => pagination(request),
            COMPONENT_COLLAPSIBLE => collapsible(request),
            _ => Err("The component host received an unknown component kind.".into()),
        }
    }
}

#[derive(Clone)]
struct RetainedTextarea {
    state: Entity<TextareaState>,
    rows: Rc<Cell<u32>>,
    placeholder: Rc<RefCell<String>>,
    events: Rc<TextareaEvents>,
    _subscription: Rc<Subscription>,
}

struct TextareaEvents {
    changed_token: Cell<u64>,
    revision: Cell<u64>,
    last_text: RefCell<Rope>,
    emitter: NativeExtensionEventEmitter,
    callback_error: Cell<Option<i32>>,
}

impl TextareaEvents {
    fn changed(&self, current: &Rope) {
        let previous = self.last_text.replace(current.clone());
        if previous == *current {
            return;
        }
        let Some(revision) = self.revision.get().checked_add(1) else {
            self.callback_error.set(Some(-86));
            return;
        };
        self.revision.set(revision);
        let token = self.changed_token.get();
        if token == 0 {
            return;
        }
        let value = current.to_string();
        if let Err(status) =
            self.emitter
                .emit(token, TEXTAREA_EVENT_CHANGED, 0, revision, value.as_bytes())
        {
            self.callback_error.set(Some(status));
        }
    }
}

fn textarea(
    request: NativeExtensionRequest,
    resources: &NativeExtensionStore,
    window: &mut Window,
    cx: &mut App,
) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = TextareaConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Textarea configuration."))?;
    if !(1..=1000).contains(&config.rows) {
        return Err("Textarea rows must be 1 through 1000.".into());
    }
    let resource = resources.get_or_insert_with(&request.resource_key, || {
        let state = cx.new(|cx| {
            TextareaState::new(window, cx)
                // Plain rows leave the outer input at a one-line minimum height.
                .auto_grow(config.rows as usize, config.rows as usize)
                .placeholder(config.placeholder.to_owned())
                .default_value(config.initial_value.clone())
        });
        let events = Rc::new(TextareaEvents {
            changed_token: Cell::new(config.changed_event),
            revision: Cell::new(0),
            last_text: RefCell::new(state.read(cx).text().clone()),
            emitter: request.events,
            callback_error: Cell::new(None),
        });
        let dispatch = events.clone();
        let observed_state = state.clone();
        let subscription = window.subscribe(&state, cx, move |_, emitted: &InputEvent, _, cx| {
            if matches!(emitted, InputEvent::Change) {
                dispatch.changed(observed_state.read(cx).text());
            }
        });
        RetainedTextarea {
            state,
            rows: Rc::new(Cell::new(config.rows)),
            placeholder: Rc::new(RefCell::new(config.placeholder.to_owned())),
            events,
            _subscription: Rc::new(subscription),
        }
    });
    resource.events.changed_token.set(config.changed_event);
    if let Some(status) = resource.events.callback_error.take() {
        return Err(
            format!("The managed Textarea event callback failed with status {status}.").into(),
        );
    }
    for command in request.commands {
        match command.command {
            TEXTAREA_COMMAND_FOCUS => resource
                .state
                .update(cx, |state, cx| state.focus(window, cx)),
            TEXTAREA_COMMAND_SET_VALUE => {
                let value = std::str::from_utf8(&command.payload)
                    .map_err(|_| SharedString::from("The Textarea value is not UTF-8."))?;
                if resource.state.read(cx).text().to_string() != value {
                    resource.state.update(cx, |state, cx| {
                        state.set_value(value.to_owned(), window, cx)
                    });
                    resource
                        .events
                        .last_text
                        .replace(resource.state.read(cx).text().clone());
                    resource.events.revision.set(
                        resource
                            .events
                            .revision
                            .get()
                            .checked_add(1)
                            .ok_or_else(|| SharedString::from("Textarea revision overflow."))?,
                    );
                }
            }
            _ => return Err("The Textarea provider received an unknown command.".into()),
        }
    }
    if let Some(status) = resource.events.callback_error.take() {
        return Err(
            format!("The managed Textarea event callback failed with status {status}.").into(),
        );
    }
    if resource.rows.replace(config.rows) != config.rows {
        resource.state.update(cx, |state, cx| {
            state.set_auto_grow(config.rows as usize, config.rows as usize, cx)
        });
    }
    if resource.placeholder.borrow().as_str() != config.placeholder {
        resource.state.update(cx, |state, cx| {
            state.set_placeholder(config.placeholder.to_owned(), window, cx)
        });
        *resource.placeholder.borrow_mut() = config.placeholder.to_owned();
    }
    let mut element = Textarea::new(&resource.state)
        .disabled(config.disabled)
        .readonly(config.read_only)
        .w_full();
    if !config.accessibility_label.is_empty() {
        element = element.aria_label(config.accessibility_label.to_owned());
    }
    Ok(element.into_any_element())
}

fn form(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = FormConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Form configuration."))?;
    validate_form(&config, request.children.len())?;

    let mut children = request.children.into_iter();
    let mut fields = Vec::with_capacity(config.labels.len());
    for index in 0..config.labels.len() {
        let mut field = Field::new()
            .label(config.labels[index].clone())
            .required(config.required[index] != 0)
            .col_span(config.column_spans[index] as u16);
        if !config.error_texts[index].is_empty() {
            let error: SharedString = config.error_texts[index].clone().into();
            field = field.description_fn(move |_, cx| {
                div().text_color(cx.theme().danger).child(error.clone())
            });
        } else if !config.help_texts[index].is_empty() {
            field = field.description(config.help_texts[index].clone());
        }
        let control = children.next().ok_or("Missing Form control child.")?;
        fields.push(field.child(control));
    }

    let mut component = Form::new()
        .with_size(match config.size {
            FormSize::Xsmall => gpui_component::Size::XSmall,
            FormSize::Small => gpui_component::Size::Small,
            FormSize::Medium => gpui_component::Size::Medium,
            FormSize::Large => gpui_component::Size::Large,
        })
        .label_layout(match config.label_axis {
            FormLabelAxis::Horizontal => Axis::Horizontal,
            FormLabelAxis::Vertical => Axis::Vertical,
        })
        .label_width(px(config.label_width_pixels))
        .columns(config.columns as usize)
        .children(fields);
    if config.has_footer {
        component = component.footer(children.next().ok_or("Missing Form footer child.")?);
    }
    Ok(component.into_any_element())
}

fn validate_form(config: &FormConfiguration, child_count: usize) -> Result<(), SharedString> {
    let count = config.labels.len();
    if !(1..=4).contains(&config.columns)
        || !config.label_width_pixels.is_finite()
        || config.label_width_pixels <= 0.0
        || count > 256
        || config.help_texts.len() != count
        || config.error_texts.len() != count
        || config.required.len() != count
        || config.column_spans.len() != count
        || config.labels.iter().any(|label| label.trim().is_empty())
        || config.labels.iter().any(|label| label.contains('\0'))
        || config.help_texts.iter().any(|text| text.contains('\0'))
        || config.error_texts.iter().any(|text| text.contains('\0'))
        || config.required.iter().any(|value| *value > 1)
        || config
            .column_spans
            .iter()
            .any(|span| *span == 0 || *span > config.columns)
        || count.checked_add(usize::from(config.has_footer)) != Some(child_count)
    {
        return Err("Invalid Form field batch or layout.".into());
    }
    Ok(())
}

fn attachment(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = AttachmentConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Attachment configuration."))?;
    let [media_child, content_child, actions_child] = split_slots(
        request.children,
        [config.media_child, config.content_child, config.has_actions],
    )?;
    let mut component = Attachment::new()
        .with_size(match config.size {
            AttachmentSize::Xsmall => gpui_component::Size::XSmall,
            AttachmentSize::Small => gpui_component::Size::Small,
            AttachmentSize::Medium => gpui_component::Size::Medium,
            AttachmentSize::Large => gpui_component::Size::Large,
        })
        .axis(match config.axis {
            AttachmentAxis::Horizontal => Axis::Horizontal,
            AttachmentAxis::Vertical => Axis::Vertical,
        })
        .status(match config.status {
            AttachmentStatus::Pending => NativeAttachmentStatus::Pending,
            AttachmentStatus::Uploading => NativeAttachmentStatus::Uploading,
            AttachmentStatus::Processing => NativeAttachmentStatus::Processing,
            AttachmentStatus::Failed => NativeAttachmentStatus::Failed,
            AttachmentStatus::Complete => NativeAttachmentStatus::Complete,
        });

    if !config.preview_source.is_empty() || media_child.is_some() {
        let mut media = AttachmentMedia::new();
        if !config.preview_source.is_empty() {
            media = media.src(config.preview_source.to_owned());
        }
        media.extend(media_child);
        component = component.media(media);
    }
    if !config.title.is_empty() || !config.description.is_empty() || content_child.is_some() {
        let mut content = AttachmentContent::new();
        if !config.title.is_empty() {
            content = content.title(AttachmentTitle::new(config.title.to_owned()));
        }
        if !config.description.is_empty() {
            content =
                content.description(AttachmentDescription::new(config.description.to_owned()));
        }
        content.extend(content_child);
        component = component.content(content);
    }
    if let Some(actions_child) = actions_child {
        let mut actions = AttachmentActions::new();
        actions.extend([actions_child]);
        component = component.actions(actions);
    }
    if config.clicked_event != 0 {
        let token = config.clicked_event;
        let events = request.events;
        component = component
            .id(request.resource_key.key().to_owned())
            .on_click(move |_, _, _| {
                let _ = events.emit(token, ATTACHMENT_EVENT_CLICKED, 0, 0, &[]);
            });
    }
    Ok(component.into_any_element())
}

fn empty(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = EmptyConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Empty configuration."))?;
    let [media_child, content_child, footer_child] = split_slots(
        request.children,
        [config.has_media, config.has_content, config.has_footer],
    )?;

    let mut component = ComponentEmpty::new();
    if media_child.is_some() || !config.title.is_empty() || !config.description.is_empty() {
        let mut header = EmptyHeader::new();
        if let Some(media_child) = media_child {
            let mut media = EmptyMedia::new().with_variant(match config.media_variant {
                EmptyMediaVariant::Default => NativeEmptyMediaVariant::Default,
                EmptyMediaVariant::Icon => NativeEmptyMediaVariant::Icon,
            });
            media.extend([media_child]);
            header = header.media(media);
        }
        if !config.title.is_empty() {
            let mut title = EmptyTitle::new();
            title.extend([config.title.to_owned().into_any_element()]);
            header = header.title(title);
        }
        if !config.description.is_empty() {
            let mut description = EmptyDescription::new();
            description.extend([config.description.to_owned().into_any_element()]);
            header = header.description(description);
        }
        component = component.header(header);
    }
    if let Some(content_child) = content_child {
        let mut content = EmptyContent::new();
        content.extend([content_child]);
        component = component.content(content);
    }
    component.extend(footer_child);
    Ok(component.into_any_element())
}

fn split_slots<const N: usize>(
    children: Vec<AnyElement>,
    present: [bool; N],
) -> Result<[Option<AnyElement>; N], SharedString> {
    if children.len() != present.into_iter().filter(|value| *value).count() {
        return Err("Component slot count does not match its configuration.".into());
    }
    let mut children = children.into_iter();
    let mut slots = std::array::from_fn(|_| None);
    for (slot, present) in slots.iter_mut().zip(present) {
        if present {
            *slot = children.next();
        }
    }
    Ok(slots)
}

fn description_list(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = DescriptionListConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid DescriptionList configuration."))?;
    if !(1..=10).contains(&config.columns) || config.label_width_pixels <= 0.0 {
        return Err("Invalid DescriptionList layout.".into());
    }
    let items = description_items(&config.entry_spans, request.children, config.columns)?;
    let mut component = DescriptionList::new()
        .with_size(match config.size {
            DescriptionListSize::Xsmall => gpui_component::Size::XSmall,
            DescriptionListSize::Small => gpui_component::Size::Small,
            DescriptionListSize::Medium => gpui_component::Size::Medium,
            DescriptionListSize::Large => gpui_component::Size::Large,
        })
        .layout(match config.axis {
            DescriptionListAxis::Horizontal => Axis::Horizontal,
            DescriptionListAxis::Vertical => Axis::Vertical,
        })
        .label_width(px(config.label_width_pixels))
        .bordered(config.bordered)
        .columns(config.columns as usize);
    component = component.children(items);
    Ok(component.into_any_element())
}

fn description_items(
    spans: &[u32],
    children: Vec<AnyElement>,
    columns: u32,
) -> Result<Vec<DescriptionItem>, SharedString> {
    if spans.iter().any(|span| *span > columns) {
        return Err("A DescriptionList entry span exceeds its column count.".into());
    }
    let item_count = spans.iter().filter(|span| **span != 0).count();
    if item_count.checked_mul(2) != Some(children.len()) {
        return Err("DescriptionList child count does not match its entries.".into());
    }
    let mut children = children.into_iter();
    let mut items = Vec::with_capacity(spans.len());
    for span in spans {
        if *span == 0 {
            items.push(DescriptionItem::Separator);
        } else {
            let label = children.next().ok_or("Missing DescriptionList label.")?;
            let value = children.next().ok_or("Missing DescriptionList value.")?;
            items.push(
                DescriptionItem::new(label)
                    .value(value)
                    .span(*span as usize),
            );
        }
    }
    Ok(items)
}

fn breadcrumb(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = BreadcrumbConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Breadcrumb configuration."))?;
    validate_breadcrumb(&config)?;
    let mut component = Breadcrumb::new();
    for ((label, id), disabled) in config
        .labels
        .into_iter()
        .zip(config.item_ids)
        .zip(config.item_disabled)
    {
        let mut item = BreadcrumbItem::new(label).disabled(disabled != 0);
        if config.clicked_event != 0 {
            let events = request.events;
            let token = config.clicked_event;
            item = item.on_click(move |_, _, _| {
                let payload = id.to_le_bytes();
                let _ = events.emit(token, BREADCRUMB_EVENT_CLICKED, 0, 0, &payload);
            });
        }
        component = component.child(item);
    }
    Ok(component.into_any_element())
}

struct TabsInteraction {
    focus: FocusHandle,
    // Key repeats advance locally until the next managed selection snapshot is accepted.
    cursor: Cell<Option<u32>>,
}

fn tabs(
    request: NativeExtensionRequest,
    resources: &NativeExtensionStore,
    cx: &mut App,
) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = TabsConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Tabs configuration."))?;
    validate_tabs(&config)?;
    let interaction = resources.get_or_insert_with(&request.resource_key, || {
        Rc::new(TabsInteraction {
            focus: cx.focus_handle(),
            cursor: Cell::new(None),
        })
    });
    let selected_id = config.has_selection.then_some(config.selected_id);
    interaction.cursor.set(selected_id);
    let keyboard_enabled = config.selected_event != 0 && config.item_disabled.contains(&0);
    let focus = interaction.focus.clone().tab_stop(keyboard_enabled);
    let selected_index = config
        .has_selection
        .then(|| {
            config
                .item_ids
                .iter()
                .position(|id| *id == config.selected_id)
        })
        .flatten();
    let mut component = TabBar::new(format!(
        "gpui-net-tabs:{}:{}",
        request.resource_key.owner_view(),
        request.resource_key.key()
    ))
    .with_size(match config.size {
        TabsSize::Xsmall => gpui_component::Size::XSmall,
        TabsSize::Small => gpui_component::Size::Small,
        TabsSize::Medium => gpui_component::Size::Medium,
        TabsSize::Large => gpui_component::Size::Large,
    })
    .with_variant(match config.variant {
        TabsVariant::Tab => NativeTabVariant::Tab,
        TabsVariant::Outline => NativeTabVariant::Outline,
        TabsVariant::Pill => NativeTabVariant::Pill,
        TabsVariant::Segmented => NativeTabVariant::Segmented,
        TabsVariant::Underline => NativeTabVariant::Underline,
    })
    .menu(config.overflow_menu);
    if let Some(index) = selected_index {
        component = component.selected_index(index);
    }
    if config.selected_event != 0 {
        let ids = config.item_ids.clone();
        let events = request.events;
        let token = config.selected_event;
        let interaction = interaction.clone();
        component = component.on_click(move |index, window, cx| {
            if let Some(id) = ids.get(*index) {
                interaction.cursor.set(Some(*id));
                interaction.focus.focus(window, cx);
                let payload = id.to_le_bytes();
                let _ = events.emit(token, TABS_EVENT_SELECTED, 0, 0, &payload);
            }
        });
    }
    let ids = config.item_ids;
    let disabled_items = config.item_disabled;
    for (label, disabled) in config.labels.into_iter().zip(&disabled_items) {
        component = component.child(Tab::new().label(label).disabled(*disabled != 0));
    }
    let events = request.events;
    let token = config.selected_event;
    let ring = cx.theme().ring;
    let transparent = cx.theme().transparent;
    // A drop shadow shows through the transparent tab bar as a solid accent fill.
    let element = div()
        .id(format!(
            "gpui-net-tabs-focus:{}:{}",
            request.resource_key.owner_view(),
            request.resource_key.key()
        ))
        .track_focus(&focus)
        .border_1()
        .border_color(transparent)
        .focus_visible(move |style| style.border_color(ring))
        .on_key_down(move |event: &KeyDownEvent, window, cx| {
            if !keyboard_enabled || !focus.is_focused(window) {
                return;
            }
            let modifiers = event.keystroke.modifiers;
            if modifiers.control
                || modifiers.alt
                || modifiers.platform
                || modifiers.function
                || modifiers.shift
            {
                return;
            }
            let current = interaction.cursor.get();
            let Some(id) =
                tabs_key_target(event.keystroke.key.as_str(), current, &ids, &disabled_items)
            else {
                return;
            };
            cx.stop_propagation();
            if current == Some(id) && !matches!(event.keystroke.key.as_str(), "enter" | "space") {
                return;
            }
            interaction.cursor.set(Some(id));
            let payload = id.to_le_bytes();
            let _ = events.emit(token, TABS_EVENT_SELECTED, 0, 0, &payload);
        })
        .child(component);
    Ok(element.into_any_element())
}

fn tabs_key_target(key: &str, current: Option<u32>, ids: &[u32], disabled: &[u32]) -> Option<u32> {
    if ids.len() != disabled.len() || ids.is_empty() {
        return None;
    }
    match key {
        "home" => ids
            .iter()
            .zip(disabled)
            .find(|(_, disabled)| **disabled == 0)
            .map(|(id, _)| *id),
        "end" => ids
            .iter()
            .zip(disabled)
            .rfind(|(_, disabled)| **disabled == 0)
            .map(|(id, _)| *id),
        "enter" | "space" => match current {
            Some(id) => ids
                .iter()
                .position(|item| *item == id)
                .filter(|index| disabled[*index] == 0)
                .map(|_| id),
            None => tabs_key_target("home", None, ids, disabled),
        },
        "left" | "right" => {
            let forward = key == "right";
            let Some(mut index) = current.and_then(|id| ids.iter().position(|item| *item == id))
            else {
                return if forward {
                    tabs_key_target("home", current, ids, disabled)
                } else {
                    tabs_key_target("end", current, ids, disabled)
                };
            };
            for _ in 0..ids.len() {
                index = if forward {
                    (index + 1) % ids.len()
                } else if index == 0 {
                    ids.len() - 1
                } else {
                    index - 1
                };
                if disabled[index] == 0 {
                    return Some(ids[index]);
                }
            }
            None
        }
        _ => None,
    }
}

fn validate_tabs(config: &TabsConfiguration) -> Result<(), SharedString> {
    let len = config.labels.len();
    if config.item_ids.len() != len || config.item_disabled.len() != len {
        return Err("Tab item batches have different lengths.".into());
    }
    let mut ids = HashSet::with_capacity(len);
    for ((label, id), disabled) in config
        .labels
        .iter()
        .zip(&config.item_ids)
        .zip(&config.item_disabled)
    {
        if label.trim().is_empty() || *disabled > 1 || !ids.insert(*id) {
            return Err("Tabs contain an invalid label, ID, or disabled flag.".into());
        }
    }
    if config.has_selection && !ids.contains(&config.selected_id) {
        return Err("The selected tab ID is absent from the items.".into());
    }
    Ok(())
}

fn validate_breadcrumb(config: &BreadcrumbConfiguration) -> Result<(), SharedString> {
    let len = config.labels.len();
    if config.item_ids.len() != len || config.item_disabled.len() != len {
        return Err("Breadcrumb item batches have different lengths.".into());
    }
    let mut ids = HashSet::with_capacity(len);
    for ((label, id), disabled) in config
        .labels
        .iter()
        .zip(&config.item_ids)
        .zip(&config.item_disabled)
    {
        if label.trim().is_empty() || *disabled > 1 || !ids.insert(*id) {
            return Err("Breadcrumb items contain an invalid label, ID, or disabled flag.".into());
        }
    }
    Ok(())
}

fn toolbar(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = ToolbarConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Toolbar configuration."))?;
    let component = Toolbar::new(request.resource_key.key().to_owned())
        .with_size(match config.size {
            ToolbarSize::Xsmall => gpui_component::Size::XSmall,
            ToolbarSize::Small => gpui_component::Size::Small,
            ToolbarSize::Medium => gpui_component::Size::Medium,
            ToolbarSize::Large => gpui_component::Size::Large,
        })
        .disabled(config.disabled)
        .contents(request.children);
    Ok(component.into_any_element())
}

fn toolbar_group(mut request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = ToolbarGroupConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid ToolbarGroup configuration."))?;
    let mut component = ToolbarGroup::new(request.resource_key.key().to_owned());
    if !config.label.is_empty() {
        component = component.label(config.label.to_owned());
    }
    component.extend(request.children.drain(..));
    Ok(component.into_any_element())
}

fn status_bar(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = StatusBarConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid StatusBar configuration."))?;
    let [left, center, right] = split_slots(
        request.children,
        [config.has_left, config.has_center, config.has_right],
    )?;
    let mut component = StatusBar::new();
    if let Some(left) = left {
        component = component.left(left);
    }
    component.extend(center);
    if let Some(right) = right {
        component = component.right(right);
    }
    Ok(component.into_any_element())
}

fn bubble(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = BubbleConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Bubble configuration."))?;
    let [content, reactions] =
        split_slots(request.children, [config.has_content, config.has_reactions])?;
    let mut component = Bubble::new().with_variant(match config.variant {
        BubbleVariant::Filled => NativeBubbleVariant::Filled,
        BubbleVariant::Secondary => NativeBubbleVariant::Secondary,
        BubbleVariant::Muted => NativeBubbleVariant::Muted,
        BubbleVariant::Tinted => NativeBubbleVariant::Tinted,
        BubbleVariant::Outline => NativeBubbleVariant::Outline,
        BubbleVariant::Ghost => NativeBubbleVariant::Ghost,
        BubbleVariant::Destructive => NativeBubbleVariant::Destructive,
    });
    component = match config.alignment {
        BubbleAlignment::Inherit => component,
        BubbleAlignment::Start => component.alignment(NativeMessageAlignment::Start),
        BubbleAlignment::End => component.alignment(NativeMessageAlignment::End),
    };
    component.extend(content);
    if let Some(reactions) = reactions {
        let mut region = BubbleReactions::new()
            .side(match config.reaction_side {
                BubbleReactionSide::Top => NativeBubbleReactionSide::Top,
                BubbleReactionSide::Bottom => NativeBubbleReactionSide::Bottom,
            })
            .alignment(match config.reaction_alignment {
                BubbleReactionAlignment::Start => NativeMessageAlignment::Start,
                BubbleReactionAlignment::End => NativeMessageAlignment::End,
            });
        region.extend([reactions]);
        component = component.reactions(region);
    }
    Ok(component.into_any_element())
}

fn bubble_group(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    BubbleGroupConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid BubbleGroup configuration."))?;
    let mut component = BubbleGroup::new();
    component.extend(request.children);
    Ok(component.into_any_element())
}

fn message(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = MessageConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Message configuration."))?;
    let [avatar, header, content, footer] = split_slots(
        request.children,
        [
            config.has_avatar,
            config.has_header,
            config.has_content,
            config.has_footer,
        ],
    )?;
    let mut component = Message::new()
        .id(request.resource_key.key().to_owned())
        .alignment(match config.alignment {
            MessageAlignment::Start => NativeMessageAlignment::Start,
            MessageAlignment::End => NativeMessageAlignment::End,
        });
    if config.accessible_list_item {
        component = component.role(Role::ListItem);
    }
    if let Some(avatar) = avatar {
        let mut slot = MessageAvatar::new();
        slot.extend([avatar]);
        component = component.avatar_slot(slot);
    }
    if let Some(header) = header {
        let mut slot = MessageHeader::new().content_inset(!config.content_has_ghost_surface);
        slot.extend([header]);
        component = component.header(slot);
    }
    if let Some(content) = content {
        let mut slot = MessageContent::new();
        slot.extend([content]);
        component = component.content(slot);
    }
    if let Some(footer) = footer {
        let mut slot = MessageFooter::new().content_inset(!config.content_has_ghost_surface);
        slot.extend([footer]);
        component = component.footer(slot);
    }
    Ok(component.into_any_element())
}

fn message_group(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    MessageGroupConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid MessageGroup configuration."))?;
    let mut component = MessageGroup::new();
    component.extend(request.children);
    Ok(component.into_any_element())
}

fn marker(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = MarkerConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Marker configuration."))?;
    let [icon, extra] = split_slots(request.children, [config.has_icon, config.has_extra])?;
    let mut component = Marker::new()
        .with_variant(match config.variant {
            MarkerVariant::Plain => NativeMarkerVariant::Plain,
            MarkerVariant::Separator => NativeMarkerVariant::Separator,
            MarkerVariant::Border => NativeMarkerVariant::Border,
        })
        .loading(config.loading)
        .with_loading_style(match config.loading_style {
            MarkerLoadingStyle::Spinner => NativeMarkerLoadingStyle::Spinner,
            MarkerLoadingStyle::Shimmer => NativeMarkerLoadingStyle::Shimmer,
        });
    component = match config.alignment {
        MarkerAlignment::Inherit => component,
        MarkerAlignment::Start => component.alignment(NativeMarkerAlignment::Start),
        MarkerAlignment::Center => component.alignment(NativeMarkerAlignment::Center),
        MarkerAlignment::End => component.alignment(NativeMarkerAlignment::End),
    };
    if config.status_role {
        component = component
            .id(request.resource_key.key().to_owned())
            .role(Role::Status);
    }
    if let Some(icon) = icon {
        let mut slot = MarkerIcon::new();
        slot.extend([icon]);
        component = component.icon(slot);
    }
    if !config.text.is_empty() {
        component = component.content(MarkerContent::new().text(config.text.to_owned()));
    }
    component.extend(extra);
    Ok(component.into_any_element())
}

fn no_children(request: &NativeExtensionRequest) -> Result<(), SharedString> {
    if request.children.is_empty() {
        Ok(())
    } else {
        Err("This component does not accept child elements.".into())
    }
}

fn optional_color(value: &str) -> Result<Option<Hsla>, SharedString> {
    if value.is_empty() {
        return Ok(None);
    }
    try_parse_color(value)
        .map(Some)
        .map_err(|error| format!("Invalid component color '{value}': {error}").into())
}

fn spinner(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = SpinnerConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Spinner configuration."))?;
    let mut component = Spinner::new()
        .with_size(match config.size {
            SpinnerSize::Xsmall => gpui_component::Size::XSmall,
            SpinnerSize::Small => gpui_component::Size::Small,
            SpinnerSize::Medium => gpui_component::Size::Medium,
            SpinnerSize::Large => gpui_component::Size::Large,
        })
        .icon(match config.icon {
            SpinnerIcon::Loader => gpui_component::IconName::Loader,
            SpinnerIcon::LoaderCircle => gpui_component::IconName::LoaderCircle,
        });
    component = match config.ease {
        SpinnerEase::Linear => component.ease(gpui::linear),
        SpinnerEase::EaseInOut => component.ease(gpui::ease_in_out),
        SpinnerEase::EaseOutQuint => component.ease(gpui::ease_out_quint()),
    };
    if let Some(color) = optional_color(config.color)? {
        component = component.color(color);
    }
    Ok(component.into_any_element())
}

fn skeleton(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = SkeletonConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Skeleton configuration."))?;
    let component = if config.secondary {
        Skeleton::new().secondary()
    } else {
        Skeleton::new()
    };
    Ok(component.into_any_element())
}

fn separator(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = SeparatorConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Separator configuration."))?;
    let mut component = match (config.orientation, config.dashed) {
        (SeparatorOrientation::Horizontal, false) => Separator::horizontal(),
        (SeparatorOrientation::Horizontal, true) => Separator::horizontal_dashed(),
        (SeparatorOrientation::Vertical, false) => Separator::vertical(),
        (SeparatorOrientation::Vertical, true) => Separator::vertical_dashed(),
    };
    if !config.label.is_empty() {
        component = component.label(config.label.to_owned());
    }
    if let Some(color) = optional_color(config.color)? {
        component = component.color(color);
    }
    Ok(component.into_any_element())
}

fn badge(mut request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = BadgeConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Badge configuration."))?;
    let mut component = Badge::new()
        .with_size(badge_size(config.size))
        .count(config.count as usize)
        .max(config.max as usize);
    if config.dot {
        component = component.dot();
    }
    if let Some(color) = optional_color(config.color)? {
        component = component.color(color);
    }
    component.extend(request.children.drain(..));
    Ok(component.into_any_element())
}

fn tag(mut request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = TagConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Tag configuration."))?;
    let mut component =
        Tag::new()
            .with_size(tag_size(config.size))
            .with_variant(match config.variant {
                TagVariant::Primary => NativeTagVariant::Primary,
                TagVariant::Secondary => NativeTagVariant::Secondary,
                TagVariant::Danger => NativeTagVariant::Danger,
                TagVariant::Success => NativeTagVariant::Success,
                TagVariant::Warning => NativeTagVariant::Warning,
                TagVariant::Info => NativeTagVariant::Info,
            });
    if config.outline {
        component = component.outline();
    }
    if config.rounded_full {
        component = component.rounded_full();
    }
    component.extend(request.children.drain(..));
    Ok(component.into_any_element())
}

fn progress(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = ProgressConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Progress configuration."))?;
    let mut component = Progress::new(request.resource_key.key().to_owned())
        .with_size(progress_size(config.size))
        .value(config.value)
        .loading(config.loading);
    if !config.accessibility_label.is_empty() {
        component = component.accessibility_label(config.accessibility_label.to_owned());
    }
    if let Some(color) = optional_color(config.color)? {
        component = component.color(color);
    }
    Ok(component.into_any_element())
}

fn progress_circle(mut request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = ProgressCircleConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid ProgressCircle configuration."))?;
    let mut component = ProgressCircle::new(request.resource_key.key().to_owned())
        .with_size(progress_circle_size(config.size))
        .value(config.value)
        .loading(config.loading);
    if config.diameter < 0. {
        return Err("ProgressCircle diameter must be zero or positive.".into());
    }
    if config.diameter > 0. {
        component = component.size(px(config.diameter));
    }
    if !config.accessibility_label.is_empty() {
        component = component.accessibility_label(config.accessibility_label.to_owned());
    }
    if let Some(color) = optional_color(config.color)? {
        component = component.color(color);
    }
    component.extend(request.children.drain(..));
    Ok(component.into_any_element())
}

fn rating(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = RatingConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Rating configuration."))?;
    let mut component = Rating::new(request.resource_key.key().to_owned())
        .with_size(rating_size(config.size))
        .value(config.value as usize)
        .max(config.max as usize)
        .disabled(config.disabled);
    if let Some(color) = optional_color(config.color)? {
        component = component.color(color);
    }
    let token = config.changed_event;
    let events = request.events;
    if token != 0 {
        component = component.on_click(move |value, _, _| {
            let payload = (*value as u32).to_le_bytes();
            let _ = events.emit(token, RATING_EVENT_CHANGED, 0, 0, &payload);
        });
    }
    Ok(component.into_any_element())
}

fn button(mut request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = ButtonConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Button configuration."))?;
    let mut component = Button::new(request.resource_key.key().to_owned())
        .with_size(button_size(config.size))
        .with_variant(button_variant(config.variant))
        .disabled(config.disabled)
        .selected(config.selected)
        .loading(config.loading);
    if !config.label.is_empty() {
        component = component.label(config.label.to_owned());
    }
    if !config.accessibility_label.is_empty() {
        component = component.accessibility_label(config.accessibility_label.to_owned());
    }
    if !config.icon_asset_path.is_empty() {
        component = component.icon(Icon::default().path(config.icon_asset_path.to_owned()));
    }
    if config.outline {
        component = component.outline();
    }
    if config.compact {
        component = component.compact();
    }
    let token = config.clicked_event;
    let events = request.events;
    if token != 0 {
        component = component.on_click(move |_, _, _| {
            let _ = events.emit(token, BUTTON_EVENT_CLICKED, 0, 0, &[]);
        });
    }
    component.extend(request.children.drain(..));
    Ok(component.into_any_element())
}

fn alert(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = AlertConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Alert configuration."))?;
    let mut component = Alert::new(
        request.resource_key.key().to_owned(),
        config.message.to_owned(),
    )
    .with_size(alert_size(config.size))
    .with_variant(alert_variant(config.variant));
    if !config.title.is_empty() {
        component = component.title(config.title.to_owned());
    }
    if config.banner {
        component = component.banner();
    }
    let token = config.closed_event;
    let events = request.events;
    if token != 0 {
        component = component.on_close(move |_, _, _| {
            let _ = events.emit(token, ALERT_EVENT_CLOSED, 0, 0, &[]);
        });
    }
    Ok(component.into_any_element())
}

fn group_box(mut request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = GroupBoxConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid GroupBox configuration."))?;
    let mut component = GroupBox::new()
        .id(request.resource_key.key().to_owned())
        .with_variant(match config.variant {
            GroupBoxVariant::Normal => NativeGroupBoxVariant::Normal,
            GroupBoxVariant::Fill => NativeGroupBoxVariant::Fill,
            GroupBoxVariant::Outline => NativeGroupBoxVariant::Outline,
        });
    if !config.title.is_empty() {
        component = component.title(config.title.to_owned());
    }
    component.extend(request.children.drain(..));
    Ok(component.into_any_element())
}

fn label(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = LabelConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Label configuration."))?;
    let mut component = Label::new(config.text.to_owned()).masked(config.masked);
    if !config.secondary.is_empty() {
        component = component.secondary(config.secondary.to_owned());
    }
    if !config.highlight.is_empty() {
        component = component.highlights(if config.highlight_prefix {
            HighlightsMatch::Prefix(config.highlight.to_owned().into())
        } else {
            HighlightsMatch::Full(config.highlight.to_owned().into())
        });
    }
    Ok(component.into_any_element())
}

fn kbd(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = KbdConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Kbd configuration."))?;
    let stroke = Keystroke::parse(config.keystroke)
        .map_err(|error| SharedString::from(format!("Invalid Kbd keystroke: {error}")))?;
    let mut component = Kbd::new(stroke).appearance(config.appearance);
    if config.outline {
        component = component.outline();
    }
    Ok(component.into_any_element())
}

fn link(mut request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = LinkConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Link configuration."))?;
    let mut component = Link::new(request.resource_key.key().to_owned());
    if !config.href.is_empty() {
        component = component.href(config.href.to_owned());
    }
    let token = config.clicked_event;
    let events = request.events;
    if token != 0 {
        component = component.on_click(move |_, _, _| {
            let _ = events.emit(token, LINK_EVENT_CLICKED, 0, 0, &[]);
        });
    }
    component.extend(request.children.drain(..));
    Ok(component.into_any_element())
}

fn avatar(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = AvatarConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Avatar configuration."))?;
    let mut component = Avatar::new().with_size(avatar_size(config.size));
    if !config.name.is_empty() {
        component = component.name(config.name.to_owned());
    }
    if !config.source.is_empty() {
        component = component.src(config.source.to_owned());
    }
    Ok(component.into_any_element())
}

fn icon(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = IconConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Icon configuration."))?;
    if config.asset_path.is_empty() {
        return Err("Icon asset path cannot be empty.".into());
    }
    let mut component = Icon::default()
        .path(config.asset_path.to_owned())
        .with_size(match config.size {
            IconSize::Xsmall => gpui_component::Size::XSmall,
            IconSize::Small => gpui_component::Size::Small,
            IconSize::Medium => gpui_component::Size::Medium,
            IconSize::Large => gpui_component::Size::Large,
        });
    if let Some(color) = optional_color(config.color)? {
        component = component.text_color(color);
    }
    Ok(component.into_any_element())
}

fn shimmer_text(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = ShimmerTextConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid ShimmerText configuration."))?;
    let mut component = ShimmerText::new(config.text.to_owned())
        .id(request.resource_key.key().to_owned())
        .duration(Duration::from_millis(config.duration_ms.into()))
        .reverse(config.reverse)
        .once(config.once);
    if let Some(color) = optional_color(config.color)? {
        component = component.highlight_color(color);
    }
    Ok(component.into_any_element())
}

fn switch(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = SwitchConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Switch configuration."))?;
    let mut component = Switch::new(request.resource_key.key().to_owned())
        .with_size(switch_size(config.size))
        .checked(config.checked)
        .disabled(config.disabled);
    if !config.label.is_empty() {
        component = component.label(config.label.to_owned());
    }
    if !config.accessibility_label.is_empty() {
        component = component.accessibility_label(config.accessibility_label.to_owned());
    }
    if !config.tooltip.is_empty() {
        component = component.tooltip(config.tooltip.to_owned());
    }
    if let Some(color) = optional_color(config.color)? {
        component = component.color(color);
    }
    let token = config.changed_event;
    let events = request.events;
    if token != 0 {
        component = component.on_change(move |value, _, _| {
            let _ = events.emit(token, SWITCH_EVENT_CHANGED, 0, 0, &[u8::from(*value)]);
        });
    }
    Ok(component.into_any_element())
}

fn checkbox(mut request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = CheckboxConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Checkbox configuration."))?;
    let mut component = Checkbox::new(request.resource_key.key().to_owned())
        .with_size(checkbox_size(config.size))
        .checked(config.checked)
        .disabled(config.disabled);
    if !config.label.is_empty() {
        component = component.label(config.label.to_owned());
    }
    if !config.accessibility_label.is_empty() {
        component = component.accessibility_label(config.accessibility_label.to_owned());
    }
    if !config.tooltip.is_empty() {
        component = component.tooltip(config.tooltip.to_owned());
    }
    let token = config.changed_event;
    let events = request.events;
    if token != 0 {
        component = component.on_change(move |value, _, _| {
            let _ = events.emit(token, CHECKBOX_EVENT_CHANGED, 0, 0, &[u8::from(*value)]);
        });
    }
    component.extend(request.children.drain(..));
    Ok(component.into_any_element())
}

fn radio(mut request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = RadioConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Radio configuration."))?;
    let mut component = Radio::new(request.resource_key.key().to_owned())
        .with_size(radio_size(config.size))
        .checked(config.checked)
        .disabled(config.disabled);
    if !config.label.is_empty() {
        component = component.label(config.label.to_owned());
    }
    if !config.accessibility_label.is_empty() {
        component = component.accessibility_label(config.accessibility_label.to_owned());
    }
    if !config.tooltip.is_empty() {
        component = component.tooltip(config.tooltip.to_owned());
    }
    let token = config.changed_event;
    let events = request.events;
    if token != 0 {
        component = component.on_change(move |value, _, _| {
            let _ = events.emit(token, RADIO_EVENT_CHANGED, 0, 0, &[u8::from(*value)]);
        });
    }
    component.extend(request.children.drain(..));
    Ok(component.into_any_element())
}

fn toggle(mut request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = ToggleConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Toggle configuration."))?;
    let mut component = Toggle::new(request.resource_key.key().to_owned())
        .with_size(toggle_size(config.size))
        .with_variant(match config.variant {
            ToggleVariant::Ghost => NativeToggleVariant::Ghost,
            ToggleVariant::Outline => NativeToggleVariant::Outline,
        })
        .checked(config.checked)
        .disabled(config.disabled);
    if !config.label.is_empty() {
        component = component.label(config.label.to_owned());
    }
    if !config.tooltip.is_empty() {
        component = component.tooltip(config.tooltip.to_owned());
    }
    let token = config.changed_event;
    let events = request.events;
    if token != 0 {
        component = component.on_click(move |value, _, _| {
            let _ = events.emit(token, TOGGLE_EVENT_CHANGED, 0, 0, &[u8::from(*value)]);
        });
    }
    component.extend(request.children.drain(..));
    Ok(component.into_any_element())
}

fn pagination(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    no_children(&request)?;
    let config = PaginationConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Pagination configuration."))?;
    let mut component = Pagination::new(request.resource_key.key().to_owned())
        .with_size(pagination_size(config.size))
        .current_page(config.current_page as usize)
        .total_pages(config.total_pages as usize)
        .visible_pages(config.visible_pages as usize)
        .disabled(config.disabled);
    if config.compact {
        component = component.compact();
    }
    let token = config.changed_event;
    let events = request.events;
    if token != 0 {
        component = component.on_click(move |page, _, _| {
            let payload = (*page as u32).to_le_bytes();
            let _ = events.emit(token, PAGINATION_EVENT_CHANGED, 0, 0, &payload);
        });
    }
    Ok(component.into_any_element())
}

fn collapsible(request: NativeExtensionRequest) -> Result<AnyElement, SharedString> {
    let config = CollapsibleConfiguration::parse(&request.configuration)
        .ok_or_else(|| SharedString::from("Invalid Collapsible configuration."))?;
    let mut component = Collapsible::new()
        .open(config.open)
        .content(div().children(request.children));
    if config.animated {
        component = component.motion_id(request.resource_key.key().to_owned());
    }
    Ok(component.into_any_element())
}

fn badge_size(value: BadgeSize) -> gpui_component::Size {
    match value {
        BadgeSize::Xsmall => gpui_component::Size::XSmall,
        BadgeSize::Small => gpui_component::Size::Small,
        BadgeSize::Medium => gpui_component::Size::Medium,
        BadgeSize::Large => gpui_component::Size::Large,
    }
}

fn tag_size(value: component_schema::TagSize) -> gpui_component::Size {
    match value {
        component_schema::TagSize::Xsmall => gpui_component::Size::XSmall,
        component_schema::TagSize::Small => gpui_component::Size::Small,
        component_schema::TagSize::Medium => gpui_component::Size::Medium,
        component_schema::TagSize::Large => gpui_component::Size::Large,
    }
}

fn progress_size(value: ProgressSize) -> gpui_component::Size {
    match value {
        ProgressSize::Xsmall => gpui_component::Size::XSmall,
        ProgressSize::Small => gpui_component::Size::Small,
        ProgressSize::Medium => gpui_component::Size::Medium,
        ProgressSize::Large => gpui_component::Size::Large,
    }
}

fn progress_circle_size(value: ProgressCircleSize) -> gpui_component::Size {
    match value {
        ProgressCircleSize::Xsmall => gpui_component::Size::XSmall,
        ProgressCircleSize::Small => gpui_component::Size::Small,
        ProgressCircleSize::Medium => gpui_component::Size::Medium,
        ProgressCircleSize::Large => gpui_component::Size::Large,
    }
}

fn rating_size(value: RatingSize) -> gpui_component::Size {
    match value {
        RatingSize::Xsmall => gpui_component::Size::XSmall,
        RatingSize::Small => gpui_component::Size::Small,
        RatingSize::Medium => gpui_component::Size::Medium,
        RatingSize::Large => gpui_component::Size::Large,
    }
}

fn button_size(value: ButtonSize) -> gpui_component::Size {
    match value {
        ButtonSize::Xsmall => gpui_component::Size::XSmall,
        ButtonSize::Small => gpui_component::Size::Small,
        ButtonSize::Medium => gpui_component::Size::Medium,
        ButtonSize::Large => gpui_component::Size::Large,
    }
}

fn alert_size(value: AlertSize) -> gpui_component::Size {
    match value {
        AlertSize::Xsmall => gpui_component::Size::XSmall,
        AlertSize::Small => gpui_component::Size::Small,
        AlertSize::Medium => gpui_component::Size::Medium,
        AlertSize::Large => gpui_component::Size::Large,
    }
}

fn avatar_size(value: AvatarSize) -> gpui_component::Size {
    match value {
        AvatarSize::Xsmall => gpui_component::Size::XSmall,
        AvatarSize::Small => gpui_component::Size::Small,
        AvatarSize::Medium => gpui_component::Size::Medium,
        AvatarSize::Large => gpui_component::Size::Large,
    }
}

fn switch_size(value: SwitchSize) -> gpui_component::Size {
    match value {
        SwitchSize::Xsmall => gpui_component::Size::XSmall,
        SwitchSize::Small => gpui_component::Size::Small,
        SwitchSize::Medium => gpui_component::Size::Medium,
        SwitchSize::Large => gpui_component::Size::Large,
    }
}

fn checkbox_size(value: CheckboxSize) -> gpui_component::Size {
    match value {
        CheckboxSize::Xsmall => gpui_component::Size::XSmall,
        CheckboxSize::Small => gpui_component::Size::Small,
        CheckboxSize::Medium => gpui_component::Size::Medium,
        CheckboxSize::Large => gpui_component::Size::Large,
    }
}

fn radio_size(value: RadioSize) -> gpui_component::Size {
    match value {
        RadioSize::Xsmall => gpui_component::Size::XSmall,
        RadioSize::Small => gpui_component::Size::Small,
        RadioSize::Medium => gpui_component::Size::Medium,
        RadioSize::Large => gpui_component::Size::Large,
    }
}

fn toggle_size(value: ToggleSize) -> gpui_component::Size {
    match value {
        ToggleSize::Xsmall => gpui_component::Size::XSmall,
        ToggleSize::Small => gpui_component::Size::Small,
        ToggleSize::Medium => gpui_component::Size::Medium,
        ToggleSize::Large => gpui_component::Size::Large,
    }
}

fn pagination_size(value: PaginationSize) -> gpui_component::Size {
    match value {
        PaginationSize::Xsmall => gpui_component::Size::XSmall,
        PaginationSize::Small => gpui_component::Size::Small,
        PaginationSize::Medium => gpui_component::Size::Medium,
        PaginationSize::Large => gpui_component::Size::Large,
    }
}

fn button_variant(value: component_schema::ButtonVariant) -> NativeButtonVariant {
    match value {
        component_schema::ButtonVariant::Default => NativeButtonVariant::Default,
        component_schema::ButtonVariant::Primary => NativeButtonVariant::Primary,
        component_schema::ButtonVariant::Secondary => NativeButtonVariant::Secondary,
        component_schema::ButtonVariant::Danger => NativeButtonVariant::Danger,
        component_schema::ButtonVariant::Success => NativeButtonVariant::Success,
        component_schema::ButtonVariant::Warning => NativeButtonVariant::Warning,
        component_schema::ButtonVariant::Info => NativeButtonVariant::Info,
        component_schema::ButtonVariant::Ghost => NativeButtonVariant::Ghost,
        component_schema::ButtonVariant::Link => NativeButtonVariant::Link,
        component_schema::ButtonVariant::Text => NativeButtonVariant::Text,
    }
}

fn alert_variant(value: component_schema::AlertVariant) -> NativeAlertVariant {
    match value {
        component_schema::AlertVariant::Default => NativeAlertVariant::Default,
        component_schema::AlertVariant::Info => NativeAlertVariant::Info,
        component_schema::AlertVariant::Success => NativeAlertVariant::Success,
        component_schema::AlertVariant::Warning => NativeAlertVariant::Warning,
        component_schema::AlertVariant::Error => NativeAlertVariant::Error,
    }
}

fn project_theme(theme: ResolvedTheme, cx: &mut App) {
    let mode = if theme.dark {
        gpui_component::ThemeMode::Dark
    } else {
        gpui_component::ThemeMode::Light
    };
    if gpui_component::Theme::global(cx).mode != mode {
        gpui_component::Theme::change(mode, None, cx);
    }
    let color = |value| -> Hsla { rgba(value).into() };
    let background = color(theme.background);
    let text = color(theme.text);
    let text_muted = color(theme.text_muted);
    let text_on_accent = color(theme.text_on_accent);
    let border = color(theme.border);
    let border_variant = color(theme.border_variant);
    let border_focused = color(theme.border_focused);
    let surface = color(theme.surface_background);
    let element = color(theme.element_background);
    let element_hover = color(theme.element_hover);
    let element_active = color(theme.element_active);
    let accent = color(theme.accent);

    gpui_component::Theme::update(cx, |target| {
        let colors = &mut target.colors;
        colors.background = background;
        colors.foreground = text;
        colors.caret = text;
        colors.selection = accent.alpha(0.3);
        colors.muted = element;
        colors.muted_foreground = text_muted;
        colors.border = border;
        colors.input = border_variant;
        colors.ring = border_focused;
        colors.accent = element_hover;
        colors.accent_foreground = text;
        colors.primary = accent;
        colors.primary_foreground = text_on_accent;
        colors.primary_hover = element_hover;
        colors.primary_active = element_active;
        colors.button = element;
        colors.button_foreground = text;
        colors.button_hover = element_hover;
        colors.button_active = element_active;
        colors.popover = surface;
        colors.popover_foreground = text;
        colors.tab = surface;
        colors.tab_bar = element;
        colors.tab_active = surface;
        colors.tab_foreground = text_muted;
        colors.tab_active_foreground = text;
        colors.drag_border = border_focused;
        colors.drop_target = accent.alpha(0.2);
        colors.scrollbar = color(theme.scrollbar_track_background);
        colors.scrollbar_thumb = color(theme.scrollbar_thumb_background);
        colors.scrollbar_thumb_hover = border_focused;
    });
}

static COMPONENTS_EXTENSION: ComponentsExtension = ComponentsExtension;
static EXTENSIONS: [&dyn NativeExtension; 2] = [&COMPONENTS_EXTENSION, &EDITOR_EXTENSION];
static INSTALL: Once = Once::new();

#[unsafe(no_mangle)]
pub extern "C" fn gpui_dotnet_get_api(requested_version: u32) -> *const GpuiDotnetApiV3 {
    INSTALL.call_once(|| {
        install_native_extensions(&EXTENSIONS)
            .expect("the component host must install its extension registry exactly once");
    });
    gpui_dotnet::api(requested_version)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn generated_parsers_cover_the_catalog_contract() {
        assert!(SpinnerConfiguration::parse("medium\nloader\nlinear\n").is_some());
        assert!(
            ButtonConfiguration::parse(
                "medium\nprimary\nSave\n\n0\n0\n0\n0\n0\nicons/save.svg\n42"
            )
            .is_some()
        );
        assert!(
            ButtonConfiguration::parse("medium\nunknown\nSave\n\n0\n0\n0\n0\n0\n\n42").is_none()
        );
        assert!(ProgressConfiguration::parse("medium\nNaN\n0\n\nUpload").is_none());
        let circle =
            ProgressCircleConfiguration::parse("large\n80\n68\n0\n\nUpload progress").unwrap();
        assert_eq!(circle.diameter, 80.);
        assert_eq!(circle.value, 68.);
        assert!(ProgressCircleConfiguration::parse("large\nNaN\n68\n0\n\n").is_none());
        assert!(
            SwitchConfiguration::parse(
                "small\n1\n0\nWi-Fi\nWireless network\nNetwork state\n#336699\n17"
            )
            .is_some()
        );
        assert!(SwitchConfiguration::parse("small\n2\n0\n\n\n\n\n0").is_none());
        let checkbox = CheckboxConfiguration::parse("medium\n1\n0\nCheckbox\n\n\n19").unwrap();
        assert!(checkbox.checked);
        assert_eq!(checkbox.changed_event, 19);
        assert!(PaginationConfiguration::parse("medium\n3\n10\n5\n0\n0\n23").is_some());
        assert!(CollapsibleConfiguration::parse("1\n1").is_some());
        let accordion = AccordionConfiguration::parse(
            "medium\n10,20\n[\"First\",\"Second\"]\n0,1\n10\n0\n1\n0\n23",
        )
        .unwrap();
        assert_eq!(accordion.item_ids, [10, 20]);
        assert_eq!(accordion.open_ids, [10]);
        let calendar = CalendarConfiguration::parse(
            "medium\n739522\n4294967295\n0\n4294967295\n4294967295\n65\n1\n2\n1900\n2100\n23",
        )
        .unwrap();
        assert_eq!(calendar.number_of_months, 2);
        assert_eq!(calendar.disabled_weekdays, 65);
        assert!(DatePickerConfiguration::parse(
            "medium\n739522\n4294967295\n0\n4294967295\n4294967295\n65\n1\n1\n1900\n2100\nDelivery\n1\n0\n23"
        ).is_some());
        let attachment = AttachmentConfiguration::parse(
            "small\nvertical\nuploading\nreport.pdf\n\"Uploading\"\n\n1\n1\n1\n17",
        )
        .unwrap();
        assert_eq!(attachment.status, AttachmentStatus::Uploading);
        assert!(attachment.media_child);
        assert!(attachment.content_child);
        assert!(attachment.has_actions);
        assert!(
            AttachmentConfiguration::parse(
                "small\nvertical\nunknown\nreport.pdf\n\"Uploading\"\n\n1\n1\n1\n17"
            )
            .is_none()
        );
        let empty =
            EmptyConfiguration::parse("icon\nNo files\n\"Add a file to begin.\"\n1\n0\n1").unwrap();
        assert_eq!(empty.media_variant, EmptyMediaVariant::Icon);
        assert!(empty.has_media);
        assert!(!empty.has_content);
        assert!(empty.has_footer);
        let multiline =
            EmptyConfiguration::parse("icon\nNo files\n\"First line\\nSecond line\"\n0\n0\n0")
                .unwrap();
        assert_eq!(multiline.description, "First line\nSecond line");
        assert!(
            EmptyConfiguration::parse("icon\nNo files\n\"Invalid\\u0000text\"\n0\n0\n0").is_none()
        );
        let textarea =
            TextareaConfiguration::parse("\"First\\nSecond\"\nNotes\n4\n0\n1\nReview notes\n17")
                .unwrap();
        assert_eq!(textarea.initial_value, "First\nSecond");
        assert_eq!(textarea.rows, 4);
        assert!(textarea.read_only);
        assert!(
            TextareaConfiguration::parse("\"Invalid\\u0000text\"\nNotes\n4\n0\n0\n\n0").is_none()
        );
        let form = FormConfiguration::parse(
            "medium\nvertical\n2\n140\n[\"Account\",\"Notes\"]\n[\"Help\",\"\"]\n[\"\",\"Error\"]\n1,0\n1,2\n1",
        )
        .unwrap();
        assert!(validate_form(&form, 3).is_ok());
        assert!(validate_form(&form, 2).is_err());
        let mut invalid = form.clone();
        invalid.column_spans[1] = 3;
        assert!(validate_form(&invalid, 3).is_err());
        invalid.column_spans[1] = 2;
        invalid.required[0] = 2;
        assert!(validate_form(&invalid, 3).is_err());
        invalid.required[0] = 1;
        invalid.help_texts[0] = "Invalid\0help".into();
        assert!(validate_form(&invalid, 3).is_err());
        let toolbar = ToolbarConfiguration::parse("small\n1").unwrap();
        assert_eq!(toolbar.size, ToolbarSize::Small);
        assert!(toolbar.disabled);
        assert!(ToolbarConfiguration::parse("huge\n0").is_none());
        assert_eq!(
            ToolbarGroupConfiguration::parse("Document actions")
                .unwrap()
                .label,
            "Document actions"
        );
        let status_bar = StatusBarConfiguration::parse("1\n0\n1").unwrap();
        assert!(status_bar.has_left);
        assert!(!status_bar.has_center);
        assert!(status_bar.has_right);
        assert!(BubbleGroupConfiguration::parse("").is_some());
        assert!(BubbleGroupConfiguration::parse("unexpected").is_none());
        let bubble = BubbleConfiguration::parse("ghost\nend\ntop\nstart\n1\n1").unwrap();
        assert_eq!(bubble.variant, BubbleVariant::Ghost);
        assert_eq!(bubble.alignment, BubbleAlignment::End);
        assert!(bubble.has_reactions);
        assert!(MessageGroupConfiguration::parse("").is_some());
        let message = MessageConfiguration::parse("end\n1\n0\n1\n1\n1\n0").unwrap();
        assert_eq!(message.alignment, MessageAlignment::End);
        assert!(message.accessible_list_item);
        assert!(message.has_avatar);
        assert!(!message.has_footer);
        let marker =
            MarkerConfiguration::parse("separator\ncenter\n1\nshimmer\n1\nLoading\n0\n0").unwrap();
        assert_eq!(marker.variant, MarkerVariant::Separator);
        assert_eq!(marker.loading_style, MarkerLoadingStyle::Shimmer);
        let icon = IconConfiguration::parse("large\nicons/archive.svg\n#ffffff").unwrap();
        assert_eq!(icon.size, IconSize::Large);
        assert_eq!(icon.asset_path, "icons/archive.svg");
        assert!(IconConfiguration::parse("huge\nicons/archive.svg\n#ffffff").is_none());
        let avatar =
            AvatarConfiguration::parse("small\nAlex\nhttps://example.com/alex.png").unwrap();
        assert_eq!(avatar.source, "https://example.com/alex.png");
        assert!(AvatarConfiguration::parse("small\nAlex").is_none());
        let description =
            DescriptionListConfiguration::parse("small\nhorizontal\n120\n1\n2\n1,1,0,2").unwrap();
        assert_eq!(description.entry_spans, [1, 1, 0, 2]);
        assert!(
            DescriptionListConfiguration::parse("small\nhorizontal\n120\n1\n2\n1,,2").is_none()
        );
        assert!(DescriptionListConfiguration::parse("small\nhorizontal\nNaN\n1\n2\n1").is_none());
        let breadcrumb = BreadcrumbConfiguration::parse(
            "[\"Files\",\"R\\u00e9sum\\u00e9\\n2026\"]\n1,7\n0,1\n42",
        )
        .unwrap();
        assert_eq!(breadcrumb.labels[1], "Résumé\n2026");
        assert!(validate_breadcrumb(&breadcrumb).is_ok());
        assert!(BreadcrumbConfiguration::parse("[\"Files\",]\n1\n0\n42").is_none());
        let tabs = TabsConfiguration::parse(
            "medium\nunderline\n[\"Overview\",\"Résumé\"]\n1,7\n0,1\n1\n1\n1\n42",
        )
        .unwrap();
        assert!(validate_tabs(&tabs).is_ok());
        assert_eq!(tabs.selected_id, 1);
    }

    #[test]
    fn application_asset_paths_are_relative_and_scoped() {
        assert!(valid_app_asset_path("Assets/archive-box.svg"));
        assert!(!valid_app_asset_path(""));
        assert!(!valid_app_asset_path("../archive-box.svg"));
        assert!(!valid_app_asset_path("Assets/../archive-box.svg"));
        assert!(!valid_app_asset_path("Assets\\archive-box.svg"));
    }

    #[test]
    fn optional_slots_keep_their_named_positions_and_reject_mismatches() {
        let [media, content, footer] =
            split_slots(vec![div().into_any_element()], [false, true, false]).unwrap();
        assert!(media.is_none());
        assert!(content.is_some());
        assert!(footer.is_none());
        assert!(split_slots(vec![], [true, false, false]).is_err());
        assert!(split_slots(vec![div().into_any_element()], [false, false, false]).is_err());
        let [avatar, header, content, footer] = split_slots(
            vec![div().into_any_element(), div().into_any_element()],
            [true, false, true, false],
        )
        .unwrap();
        assert!(avatar.is_some());
        assert!(header.is_none());
        assert!(content.is_some());
        assert!(footer.is_none());
    }

    #[test]
    fn description_list_reconciles_span_batch_and_rich_children() {
        let children = || vec![div().into_any_element(), div().into_any_element()];
        let items = description_items(&[1, 0], children(), 2).unwrap();
        assert_eq!(items.len(), 2);
        assert!(matches!(items[0], DescriptionItem::Item { span: 1, .. }));
        assert!(matches!(items[1], DescriptionItem::Separator));
        assert!(description_items(&[3], children(), 2).is_err());
        assert!(description_items(&[1], vec![], 2).is_err());
        assert!(description_items(&[0], children(), 2).is_err());
    }

    #[test]
    fn breadcrumb_rejects_mismatched_and_ambiguous_item_batches() {
        let parse = |value| BreadcrumbConfiguration::parse(value).unwrap();
        assert!(validate_breadcrumb(&parse("[\"Files\"]\n1,2\n0\n0")).is_err());
        assert!(validate_breadcrumb(&parse("[\"Files\",\"Archive\"]\n1,1\n0,0\n0")).is_err());
        assert!(validate_breadcrumb(&parse("[\"Files\"]\n1\n2\n0")).is_err());
        assert!(validate_breadcrumb(&parse("[\"\"]\n1\n0\n0")).is_err());
    }

    #[test]
    fn tabs_reject_mismatched_or_ambiguous_selection_batches() {
        let parse = |value| TabsConfiguration::parse(value).unwrap();
        assert!(validate_tabs(&parse("medium\ntab\n[\"A\"]\n1,2\n0\n1\n1\n0\n0")).is_err());
        assert!(validate_tabs(&parse("medium\ntab\n[\"A\",\"B\"]\n1,1\n0,0\n1\n1\n0\n0")).is_err());
        assert!(validate_tabs(&parse("medium\ntab\n[\"A\"]\n1\n2\n1\n1\n0\n0")).is_err());
        assert!(validate_tabs(&parse("medium\ntab\n[\"A\"]\n1\n0\n2\n1\n0\n0")).is_err());
        assert!(validate_tabs(&parse("medium\ntab\n[\"\"]\n1\n0\n1\n1\n0\n0")).is_err());
    }

    #[test]
    fn tabs_keyboard_navigation_skips_disabled_items_and_wraps() {
        let ids = [10, 20, 30, 40];
        let disabled = [0, 1, 0, 0];
        assert_eq!(
            tabs_key_target("right", Some(10), &ids, &disabled),
            Some(30)
        );
        assert_eq!(
            tabs_key_target("right", Some(40), &ids, &disabled),
            Some(10)
        );
        assert_eq!(tabs_key_target("left", Some(10), &ids, &disabled), Some(40));
        assert_eq!(tabs_key_target("right", None, &ids, &disabled), Some(10));
        assert_eq!(tabs_key_target("left", None, &ids, &disabled), Some(40));
        assert_eq!(tabs_key_target("home", Some(40), &ids, &disabled), Some(10));
        assert_eq!(tabs_key_target("end", Some(10), &ids, &disabled), Some(40));
        assert_eq!(
            tabs_key_target("enter", Some(30), &ids, &disabled),
            Some(30)
        );
        assert_eq!(tabs_key_target("enter", None, &ids, &disabled), Some(10));
        assert_eq!(tabs_key_target("space", Some(20), &ids, &disabled), None);
        assert_eq!(tabs_key_target("up", Some(10), &ids, &disabled), None);
        assert_eq!(tabs_key_target("right", Some(10), &[10], &[1]), None);
        assert_eq!(tabs_key_target("right", None, &[], &[]), None);
    }

    #[test]
    fn component_host_advertises_all_bundled_schemas() {
        let api = gpui_dotnet_get_api(gpui_dotnet::abi::ABI_VERSION);
        assert!(!api.is_null());
        let api = unsafe { &*api };
        let supports = api.supports_extension.unwrap();
        let id = EXTENSION_ID.as_bytes();
        assert_eq!(
            unsafe { supports(id.as_ptr(), id.len() as i32, SCHEMA_VERSION, SCHEMA_HASH) },
            0
        );
        assert_eq!(
            unsafe {
                supports(
                    id.as_ptr(),
                    id.len() as i32,
                    SCHEMA_VERSION,
                    SCHEMA_HASH + 1,
                )
            },
            -82
        );

        let editor_id = gpui_dotnet_editor_provider::EXTENSION_ID.as_bytes();
        assert_eq!(
            unsafe {
                supports(
                    editor_id.as_ptr(),
                    editor_id.len() as i32,
                    gpui_dotnet_editor_provider::SCHEMA_VERSION,
                    gpui_dotnet_editor_provider::SCHEMA_HASH,
                )
            },
            0
        );
    }

    #[test]
    fn component_provider_exposes_its_indicator_assets() {
        let assets = COMPONENTS_EXTENSION.asset_source().unwrap();
        let check = assets.load("icons/check.svg").unwrap().unwrap();
        assert!(!check.is_empty());
        assert!(
            assets
                .list("icons/check")
                .unwrap()
                .iter()
                .any(|path| path.as_ref() == "icons/check.svg")
        );
    }

    #[gpui::test]
    fn provider_installs_component_foundation_and_projects_managed_theme(
        cx: &mut gpui::TestAppContext,
    ) {
        cx.update(|cx| {
            assert!(!cx.has_global::<gpui_component::Theme>());
            COMPONENTS_EXTENSION.initialize(cx);
            assert!(cx.has_global::<gpui_component::Theme>());

            cx.set_global(ResolvedTheme {
                dark: true,
                text: 0xF0F4F8FF,
                text_on_accent: 0x102030FF,
                accent: 0x4466EEFF,
                ..Default::default()
            });
            COMPONENTS_EXTENSION.apply_theme(cx);

            let projected = gpui_component::Theme::global(cx);
            assert_eq!(projected.mode, gpui_component::ThemeMode::Dark);
            assert_eq!(projected.colors.foreground, Hsla::from(rgba(0xF0F4F8FF)));
            assert_eq!(projected.colors.primary, Hsla::from(rgba(0x4466EEFF)));
            assert_eq!(
                projected.colors.primary_foreground,
                Hsla::from(rgba(0x102030FF))
            );
            assert_eq!(projected.tokens.primary.color, Hsla::from(rgba(0x4466EEFF)));
        });
    }
}
