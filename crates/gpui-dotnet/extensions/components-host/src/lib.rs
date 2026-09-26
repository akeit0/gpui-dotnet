use std::{borrow::Cow, path::{Component as PathComponent, Path}, sync::Once, time::Duration};

use gpui::{
    AnyElement, App, AssetSource, Axis, Hsla, IntoElement as _, Keystroke, ParentElement as _, Role, SharedString,
    Styled as _, Window, div, px, rgba,
};
use gpui_component::{
    Disableable as _, Selectable as _, Sizable as _,
    alert::{Alert, AlertVariant as NativeAlertVariant},
    attachment::{
        Attachment, AttachmentActions, AttachmentContent, AttachmentDescription, AttachmentMedia,
        AttachmentStatus as NativeAttachmentStatus, AttachmentTitle,
    },
    avatar::Avatar,
    badge::Badge,
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
    empty::{
        Empty as ComponentEmpty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia,
        EmptyMediaVariant as NativeEmptyMediaVariant, EmptyTitle,
    },
    group_box::{GroupBox, GroupBoxVariant as NativeGroupBoxVariant, GroupBoxVariants as _},
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
    tag::{Tag, TagVariant as NativeTagVariant},
    toolbar::{Toolbar, ToolbarGroup},
    Icon, try_parse_color,
};
use gpui_dotnet::{
    abi::GpuiDotnetApiV3,
    extension::{
        NativeExtension, NativeExtensionDescriptor, NativeExtensionRequest, NativeExtensionStore,
        ResolvedTheme, install_native_extensions,
    },
};
use gpui_dotnet_editor_provider::EDITOR_EXTENSION;

#[path = "component_schema.g.rs"]
mod component_schema;

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

    fn materialize(
        &self,
        request: NativeExtensionRequest,
        _resources: &NativeExtensionStore,
        _window: &mut Window,
        _cx: &mut App,
    ) -> Result<AnyElement, SharedString> {
        if !request.commands.is_empty() {
            return Err("The component catalog does not define imperative commands.".into());
        }
        match request.resource_key.component_kind() {
            COMPONENT_ATTACHMENT => attachment(request),
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
            content = content.description(AttachmentDescription::new(config.description.to_owned()));
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
    let [content, reactions] = split_slots(
        request.children,
        [config.has_content, config.has_reactions],
    )?;
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
        component = component.id(request.resource_key.key().to_owned()).role(Role::Status);
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
    let mut component = Tag::new()
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
    let mut component = Alert::new(request.resource_key.key().to_owned(), config.message.to_owned())
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
    let mut component = Icon::default().path(config.asset_path.to_owned()).with_size(match config.size {
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
        assert!(ButtonConfiguration::parse("medium\nprimary\nSave\n\n0\n0\n0\n0\n0\nicons/save.svg\n42").is_some());
        assert!(ButtonConfiguration::parse("medium\nunknown\nSave\n\n0\n0\n0\n0\n0\n\n42").is_none());
        assert!(ProgressConfiguration::parse("medium\nNaN\n0\n\nUpload").is_none());
        let circle =
            ProgressCircleConfiguration::parse("large\n80\n68\n0\n\nUpload progress").unwrap();
        assert_eq!(circle.diameter, 80.);
        assert_eq!(circle.value, 68.);
        assert!(ProgressCircleConfiguration::parse("large\nNaN\n68\n0\n\n").is_none());
        assert!(
            SwitchConfiguration::parse("small\n1\n0\nWi-Fi\nWireless network\nNetwork state\n#336699\n17")
                .is_some()
        );
        assert!(SwitchConfiguration::parse("small\n2\n0\n\n\n\n\n0").is_none());
        let checkbox = CheckboxConfiguration::parse("medium\n1\n0\nCheckbox\n\n\n19").unwrap();
        assert!(checkbox.checked);
        assert_eq!(checkbox.changed_event, 19);
        assert!(PaginationConfiguration::parse("medium\n3\n10\n5\n0\n0\n23").is_some());
        assert!(CollapsibleConfiguration::parse("1\n1").is_some());
        let attachment = AttachmentConfiguration::parse(
            "small\nvertical\nuploading\nreport.pdf\nUploading\n\n1\n1\n1\n17",
        )
        .unwrap();
        assert_eq!(attachment.status, AttachmentStatus::Uploading);
        assert!(attachment.media_child);
        assert!(attachment.content_child);
        assert!(attachment.has_actions);
        assert!(AttachmentConfiguration::parse(
            "small\nvertical\nunknown\nreport.pdf\nUploading\n\n1\n1\n1\n17"
        )
        .is_none());
        let empty = EmptyConfiguration::parse("icon\nNo files\nAdd a file to begin.\n1\n0\n1")
            .unwrap();
        assert_eq!(empty.media_variant, EmptyMediaVariant::Icon);
        assert!(empty.has_media);
        assert!(!empty.has_content);
        assert!(empty.has_footer);
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
        let marker = MarkerConfiguration::parse("separator\ncenter\n1\nshimmer\n1\nLoading\n0\n0")
            .unwrap();
        assert_eq!(marker.variant, MarkerVariant::Separator);
        assert_eq!(marker.loading_style, MarkerLoadingStyle::Shimmer);
        let icon = IconConfiguration::parse("large\nicons/archive.svg\n#ffffff").unwrap();
        assert_eq!(icon.size, IconSize::Large);
        assert_eq!(icon.asset_path, "icons/archive.svg");
        assert!(IconConfiguration::parse("huge\nicons/archive.svg\n#ffffff").is_none());
        let avatar = AvatarConfiguration::parse("small\nAlex\nhttps://example.com/alex.png")
            .unwrap();
        assert_eq!(avatar.source, "https://example.com/alex.png");
        assert!(AvatarConfiguration::parse("small\nAlex").is_none());
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
            assert_eq!(
                projected.tokens.primary.color,
                Hsla::from(rgba(0x4466EEFF))
            );
        });
    }
}
