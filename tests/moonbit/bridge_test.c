#include "bridge_internal.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

_Static_assert(sizeof(void *) == 8, "The supported GPUI hosts are 64-bit");
_Static_assert(sizeof(NodeRecord) == 12, "NodeRecord layout");
_Static_assert(sizeof(OpRecord) == 24, "OpRecord layout");
_Static_assert(sizeof(ChildRecord) == 8, "ChildRecord layout");
_Static_assert(sizeof(NativeArtifactKey) == 16, "NativeArtifactKey layout");
_Static_assert(offsetof(GpuiDotnetApiV3, invalidate_artifacts) == 80, "API tail offset");
_Static_assert(offsetof(ManagedCallbacks, menu_applied) == 96, "Callback tail offset");

static MoonHost *host;
static int render_calls;
static int seen[13];

static void le(uint8_t *p, uint64_t v, int size) {
    for (int i = 0; i < size; i++) p[i] = (uint8_t)(v >> (8 * i));
}

static int packet(uint8_t *out, int range) {
    memset(out, 0, 128);
    le(out, GPUI_MOON_PACKET_MAGIC, 4);
    le(out + 4, range ? 2 : 1, 4);
    le(out + 12, range ? 1 : 0, 4);
    le(out + 16, 5, 4);
    le(out + 32, range ? 1 : 2, 2);
    if (range) {
        le(out + 44, 2, 2);
        le(out + 52, 5, 4);
        le(out + 60, 1, 4);
        memcpy(out + 64, "hello", 5);
        return 69;
    }
    le(out + 40, 5, 4);
    memcpy(out + 44, "hello", 5);
    return 49;
}

static int32_t dispatch(int32_t kind, uint64_t id, uint64_t a, uint64_t b,
    uint32_t x, uint32_t y, int32_t status, void *request) {
    (void)status;
    assert(kind > 0 && kind < 13);
    seen[kind]++;
    uint8_t buffer[128];
    if (kind == GPUI_MOON_CB_STARTED) {
        assert(id == 1);
        assert(gpui_moon_notify(host, 99) == GPUI_MOON_ERR_CALLBACK);
        assert(gpui_moon_application_command(host, 1, 7, 1, 2, (const uint8_t *)"test", 4, 0, 0, 800, 600) == 0);
        assert(gpui_moon_resource_command(host, 7, 1, 3, 302, (const uint8_t *)"key", 3, (const uint8_t *)"value", 5, 0, 0) == 0);
        assert(gpui_moon_extension_command(host, 7, 1, 1, 0, 1, 2, 0, (const uint8_t *)"test", 4,
            (const uint8_t *)"box", 3, (const uint8_t *)"key", 3, NULL, 0) == 0);
        le(buffer, 42, 8); le(buffer + 8, 2, 8);
        assert(gpui_moon_invalidate_artifacts(host, 7, buffer, 16) == 0);
        memset(buffer, 0, sizeof(buffer));
        le(buffer, 1, 4); le(buffer + 4, UINT32_MAX, 4); le(buffer + 8, 1, 2);
        le(buffer + 24, 4, 4); memcpy(buffer + 28, "File", 4);
        assert(gpui_moon_menu(host, 1, 1, buffer, 32) == 0);
        return 0;
    }
    if (kind == GPUI_MOON_CB_RENDER) {
        int length = packet(buffer, 0);
        render_calls++;
        int32_t result = gpui_moon_reply(request, buffer, length, render_calls == 1 ? 1 : 99);
        if (render_calls == 1) {
            assert(result == 0);
            assert(gpui_moon_reply(request, buffer, length, 100) == GPUI_MOON_ERR_PUBLICATION);
        }
        return result;
    }
    if (kind == GPUI_MOON_CB_RANGE) {
        assert(id == 7 && a == 0x100000001ull && x == 10 && y == 1);
        int length = packet(buffer, 1);
        assert(gpui_moon_reply(request, buffer, length, b == 42 ? 2 : 3) == 0);
        return b == 42 ? 0 : -777;
    }
    if (kind == GPUI_MOON_CB_CLICK) {
        assert(id == 7 && a == 0x180000001ull && b == 55);
        assert(gpui_moon_event_x(request) == 1.5f && gpui_moon_event_y(request) == 2.5f);
        assert(gpui_moon_event_buttons(request) == 3 && gpui_moon_event_modifiers(request) == 4);
    }
    if (kind == GPUI_MOON_CB_CONTROL) {
        assert(id == 7 && a == 0x180000002ull && b == 9 && x == 100 && y == 0);
        assert(gpui_moon_event_data_length(request) == 5);
        assert(gpui_moon_event_data_copy(request, buffer, 4) == GPUI_MOON_ERR_ARGUMENT);
        assert(gpui_moon_event_data_copy(request, buffer, 5) == 0);
        assert(!memcmp(buffer, "event", 5));
    }
    return 0;
}

static void parser_tests(void) {
    uint8_t bytes[128];
    int length = packet(bytes, 0);
    MoonFrame *frame = NULL;
    assert(moon_frame_decode(bytes, length, &frame) == 0);
    assert(frame->arena.node_length == 1 && frame->arena.utf8_length == 5);
    assert(!memcmp(frame->arena.utf8, "hello", 5));
    moon_frame_free(frame);
    for (int n = 0; n < length; n++) {
        frame = NULL;
        assert(moon_frame_decode(bytes, n, &frame) != 0 && frame == NULL);
    }
    le(bytes + 24, 1, 4);
    assert(moon_frame_decode(bytes, length, &frame) == GPUI_MOON_ERR_PACKET);
    packet(bytes, 0); le(bytes + 4, UINT32_MAX, 4);
    assert(moon_frame_decode(bytes, length, &frame) != 0);
    packet(bytes, 0); le(bytes + 20, 1, 4);
    assert(moon_frame_decode(bytes, length, &frame) != 0);
    assert(moon_frame_decode(NULL, -1, &frame) != 0);
    /* Small deterministic malformed-input corpus; also runs under ASan/UBSan. */
    uint32_t seed = 1;
    for (int i = 0; i < 4096; i++) {
        packet(bytes, i & 1);
        seed = seed * 1664525u + 1013904223u;
        bytes[seed % 32] ^= (uint8_t)(seed >> 24);
        frame = NULL;
        int32_t result = moon_frame_decode(bytes, i & 1 ? 69 : 49, &frame);
        if (result == 0) moon_frame_free(frame);
        else assert(frame == NULL);
    }
}

int main(int argc, char **argv) {
    assert(argc == 5);
    parser_tests();
    for (int mode = 1; mode <= 3; mode++) {
        MoonHost *bad = gpui_moon_host_new();
        int32_t status = gpui_moon_host_load(bad, (const uint8_t *)argv[mode + 1], (int32_t)strlen(argv[mode + 1]), GPUI_TEST_SCHEMA_HASH);
        assert(status == (mode == 1 ? GPUI_MOON_ERR_SCHEMA : GPUI_MOON_ERR_ABI));
        assert(gpui_moon_host_error_length(bad) > 0);
        gpui_moon_host_free(bad);
    }
    host = gpui_moon_host_new();
    assert(host && gpui_moon_host_is_null(NULL));
    assert(gpui_moon_notify(host, 7) == GPUI_MOON_ERR_STATE);
    assert(gpui_moon_host_load(host, (const uint8_t *)"a\0b", 3, 1) == GPUI_MOON_ERR_ARGUMENT);
    assert(gpui_moon_host_load(host, (const uint8_t *)argv[1], (int32_t)strlen(argv[1]), GPUI_TEST_SCHEMA_HASH) == 0);
    assert(gpui_moon_supports_extension(host, (const uint8_t *)"test", 4, 1, 2) == 0);
    assert(gpui_moon_supports_extension(host, (const uint8_t *)"none", 4, 1, 2) == -81);
    assert(gpui_moon_supports_extension(host, (const uint8_t *)"test", 4, 2, 2) == -82);
    assert(gpui_moon_host_run(host, 1, dispatch) == 0);
    assert(host->pending == NULL && host->dispatch == NULL && !host->running);
    for (int i = 1; i <= 12; i++) assert(seen[i] > 0);
    assert(gpui_moon_notify(host, 7) == GPUI_MOON_ERR_STATE);
    assert(gpui_moon_host_run(host, 1, dispatch) == GPUI_MOON_ERR_STATE);
    gpui_moon_host_free(host);
    puts("C bridge: layouts, loading, publication ownership, 12 callbacks, commands, reentry, threads, malformed packets: PASS");
    return 0;
}
