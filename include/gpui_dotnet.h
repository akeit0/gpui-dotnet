#ifndef GPUI_DOTNET_H
#define GPUI_DOTNET_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define GPUI_DOTNET_ABI_VERSION 1u

typedef struct gpui_node_record {
    uint16_t component;
    uint16_t flags;
    uint32_t data_offset;
    uint32_t data_length;
} gpui_node_record;

typedef struct gpui_op_record {
    uint32_t node;
    uint16_t code;
    uint16_t value_kind;
    uint64_t a;
    uint64_t b;
} gpui_op_record;

typedef struct gpui_child_record {
    uint32_t parent;
    uint32_t child;
} gpui_child_record;

typedef struct gpui_render_arena {
    gpui_node_record* nodes;
    int32_t node_length;
    int32_t node_capacity;
    gpui_op_record* ops;
    int32_t op_length;
    int32_t op_capacity;
    gpui_child_record* children;
    int32_t child_length;
    int32_t child_capacity;
    uint8_t* utf8;
    int32_t utf8_length;
    int32_t utf8_capacity;
    uint32_t generation;
    uint32_t flags;
    int32_t required_node_capacity;
    int32_t required_op_capacity;
    int32_t required_child_capacity;
    int32_t required_utf8_capacity;
} gpui_render_arena;

typedef struct gpui_click_event {
    float x;
    float y;
    uint32_t buttons;
    uint32_t modifiers;
} gpui_click_event;

typedef struct gpui_native_resource_command {
    uint32_t owner_view;
    uint16_t resource_kind;
    uint16_t command;
    const uint8_t* key;
    int32_t key_length;
    const uint8_t* data;
    int32_t data_length;
    uint32_t reserved;
    uint64_t a;
    uint64_t b;
} gpui_native_resource_command;

typedef struct gpui_native_control_event {
    uint16_t kind;
    uint16_t flags;
    uint32_t reserved;
    uint64_t revision;
    const uint8_t* data;
    int32_t data_length;
    uint32_t reserved2;
} gpui_native_control_event;

typedef struct gpui_native_application_command {
    uint64_t window_id;
    uint16_t command;
    uint16_t flags;
    uint32_t reserved;
    const uint8_t* title;
    int32_t title_length;
    uint32_t reserved2;
    float left;
    float top;
    float width;
    float height;
} gpui_native_application_command;

typedef struct gpui_native_theme_payload_v1 {
    uint32_t version;
    uint32_t background;
    uint32_t text;
    uint32_t text_muted;
    uint32_t text_placeholder;
    uint32_t text_on_accent;
    uint32_t border;
    uint32_t border_variant;
    uint32_t border_focused;
    uint32_t surface_background;
    uint32_t element_background;
    uint32_t element_hover;
    uint32_t element_active;
    uint32_t accent;
    uint32_t info;
    uint32_t info_background;
    uint32_t error;
    uint32_t scrollbar_thumb_background;
    uint32_t scrollbar_track_background;
} gpui_native_theme_payload_v1;

typedef struct gpui_native_menu_record {
    uint32_t parent;
    uint16_t kind;
    uint16_t flags;
    uint64_t action_id;
    const uint8_t* title;
    int32_t title_length;
    uint32_t reserved;
} gpui_native_menu_record;

typedef struct gpui_native_menu_command {
    const gpui_native_menu_record* items;
    int32_t item_length;
    uint32_t reserved;
    uint32_t reserved2;
} gpui_native_menu_command;

typedef int32_t (*gpui_managed_render_fn)(
    uint64_t session_id,
    gpui_render_arena* arena,
    uint32_t* root);

typedef int32_t (*gpui_managed_click_fn)(
    uint64_t session_id,
    uint64_t event_token,
    uint64_t event_payload,
    const gpui_click_event* event);

typedef int32_t (*gpui_managed_list_render_range_fn)(
    uint64_t session_id,
    uint64_t renderer_token,
    uint32_t start,
    uint32_t count,
    gpui_render_arena* arena,
    uint32_t* root);

typedef int32_t (*gpui_managed_control_event_fn)(
    uint64_t session_id,
    uint64_t event_token,
    const gpui_native_control_event* event);

typedef int32_t (*gpui_managed_application_started_fn)(uint64_t application_id);

typedef int32_t (*gpui_managed_window_closed_fn)(
    uint64_t application_id,
    uint64_t window_id,
    int32_t native_status);

typedef int32_t (*gpui_managed_menu_action_fn)(
    uint64_t application_id,
    uint64_t action_id);

typedef struct gpui_managed_callbacks {
    uint32_t struct_size;
    gpui_managed_render_fn render;
    gpui_managed_click_fn click;
    gpui_managed_list_render_range_fn list_render_range;
    gpui_managed_control_event_fn control_event;
    gpui_managed_application_started_fn application_started;
    gpui_managed_window_closed_fn window_closed;
    gpui_managed_menu_action_fn menu_action;
} gpui_managed_callbacks;

typedef int32_t (*gpui_validate_render_fn)(
    const gpui_render_arena* arena,
    uint32_t root);

typedef int32_t (*gpui_run_application_fn)(
    uint64_t application_id,
    const gpui_managed_callbacks* callbacks);

typedef int32_t (*gpui_notify_view_fn)(uint64_t session_id);

typedef int32_t (*gpui_dispatch_command_fn)(
    uint64_t session_id,
    const gpui_native_resource_command* command);

typedef int32_t (*gpui_dispatch_application_command_fn)(
    uint64_t application_id,
    const gpui_native_application_command* command);

typedef int32_t (*gpui_dispatch_application_menu_fn)(
    uint64_t application_id,
    const gpui_native_menu_command* command);

typedef struct gpui_dotnet_api_v1 {
    uint32_t struct_size;
    uint32_t abi_version;
    uint64_t schema_hash;
    gpui_validate_render_fn validate_render;
    gpui_run_application_fn run_application;
    gpui_notify_view_fn notify_view;
    gpui_dispatch_command_fn dispatch_command;
    gpui_dispatch_application_command_fn dispatch_application_command;
    gpui_dispatch_application_menu_fn dispatch_application_menu;
} gpui_dotnet_api_v1;

const gpui_dotnet_api_v1* gpui_dotnet_get_api(uint32_t requested_version);

#ifdef __cplusplus
}
#endif

#endif
