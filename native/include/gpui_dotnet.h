#ifndef GPUI_DOTNET_H
#define GPUI_DOTNET_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

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

typedef int32_t (*gpui_managed_render_fn)(
    uint64_t session_id,
    gpui_render_arena* arena,
    uint32_t* root);

typedef int32_t (*gpui_managed_click_fn)(
    uint64_t session_id,
    uint64_t event_token,
    const gpui_click_event* event);

typedef int32_t (*gpui_managed_shutdown_fn)(uint64_t session_id);

typedef struct gpui_managed_callbacks {
    uint32_t struct_size;
    gpui_managed_render_fn render;
    gpui_managed_click_fn click;
    gpui_managed_shutdown_fn shutdown;
} gpui_managed_callbacks;

typedef int32_t (*gpui_validate_render_fn)(
    const gpui_render_arena* arena,
    uint32_t root);

typedef int32_t (*gpui_run_application_fn)(
    uint64_t session_id,
    const gpui_managed_callbacks* callbacks,
    const uint8_t* title,
    int32_t title_length,
    float width,
    float height);

typedef int32_t (*gpui_notify_view_fn)(uint64_t session_id);

typedef struct gpui_dotnet_api_v2 {
    uint32_t struct_size;
    uint32_t abi_version;
    uint64_t schema_hash;
    gpui_validate_render_fn validate_render;
    gpui_run_application_fn run_application;
    gpui_notify_view_fn notify_view;
} gpui_dotnet_api_v2;

const gpui_dotnet_api_v2* gpui_dotnet_get_api(uint32_t requested_version);

#ifdef __cplusplus
}
#endif

#endif
