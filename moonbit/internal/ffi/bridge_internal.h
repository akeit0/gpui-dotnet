#ifndef GPUI_MOON_BRIDGE_INTERNAL_H
#define GPUI_MOON_BRIDGE_INTERNAL_H

#include "bridge.g.h"
#include "gpui_native.g.h"
#include <stddef.h>

#define GPUI_MOON_MAX_PACKET (256u * 1024u * 1024u)
#define GPUI_MOON_MAX_RECORDS (1024u * 1024u)
#define GPUI_MOON_REQUEST_MAGIC 0x47505251u

typedef struct MoonFrame {
    RenderArena arena;
    uint32_t root;
    uint64_t session;
    uint64_t source;
    uint64_t publication;
    int32_t kind;
    struct MoonFrame *next;
} MoonFrame;

typedef struct MoonHost {
    void *library;
    const GpuiDotnetApiV3 *api;
    GpuiMoonDispatch dispatch;
    MoonFrame *pending;
    uint64_t application;
    int32_t running;
    int32_t entered_native;
    int32_t callback_depth;
    char error[512];
} MoonHost;

typedef struct MoonRequest {
    uint32_t magic;
    MoonHost *host;
    int32_t kind;
    uint64_t session;
    uint64_t a;
    uint64_t b;
    const NativeClickEvent *click;
    const NativeControlEvent *control;
    RenderArena *arena;
    uint32_t *root;
    uint64_t *publication;
    MoonFrame *published;
} MoonRequest;

MoonHost *moon_active_host(void);
int32_t moon_require_running(MoonHost *host);
int32_t moon_host_error(MoonHost *host, int32_t status, const char *message);
int32_t moon_valid_bytes(const uint8_t *bytes, int32_t length);
uint16_t moon_read_u16(const uint8_t *bytes);
uint32_t moon_read_u32(const uint8_t *bytes);
uint64_t moon_read_u64(const uint8_t *bytes);
int32_t moon_frame_decode(const uint8_t *packet, int32_t length, MoonFrame **result);
void moon_frame_free(MoonFrame *frame);
void moon_frames_clear(MoonHost *host, uint64_t session);
int32_t moon_frame_retire(MoonHost *host, int32_t kind, uint64_t session,
                          uint64_t source, uint64_t publication, int required);

#endif
