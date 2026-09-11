/* A deterministic native ABI peer: no GPUI, window server, Rust, or MoonBit runtime. */
#include "gpui_native.g.h"
#include "bridge.g.h"
#include <assert.h>
#include <string.h>
#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#define EXPORT __declspec(dllexport)
#else
#include <pthread.h>
#define EXPORT __attribute__((visibility("default")))
#endif

#ifndef FAKE_MODE
#define FAKE_MODE 0
#endif
#ifndef GPUI_TEST_SCHEMA_HASH
#error Define GPUI_TEST_SCHEMA_HASH from the generated semantic protocol
#endif

static ManagedCallbacks callbacks;
static int commands;
static int menus;

static int32_t validate(const RenderArena *arena, uint32_t root) {
    if (!arena || arena->node_length < 1 || root >= (uint32_t)arena->node_length) return -901;
    if (arena->nodes[root].component == 0) return -902;
    return 0;
}

static int32_t notify(uint64_t session) {
    if (session == 99) return callbacks.dynamic_frame(99, 1); /* deliberate same-thread reentry */
    return session == 7 ? 0 : -903;
}
static int32_t resource(uint64_t session, const NativeResourceCommand *c) {
    assert(session == 7 && c && c->owner_view == 1 && c->reserved == 0);
    assert(c->key_length == 3 && !memcmp(c->key, "key", 3));
    commands++;
    return 0;
}
static int32_t application(uint64_t app, const NativeApplicationCommand *c) {
    assert(app == 1 && c && c->reserved == 0 && c->reserved2 == 0);
    commands++;
    return 0;
}
static int32_t menu(uint64_t app, const NativeMenuCommand *c) {
    assert(app == 1 && c && c->generation == 1 && c->item_length == 1);
    assert(c->reserved == 0 && c->reserved2 == 0);
    assert(c->items[0].title_length == 4 && !memcmp(c->items[0].title, "File", 4));
    menus++;
    return 0;
}
static int32_t supports(const uint8_t *id, int32_t length, uint32_t version, uint64_t hash) {
    if (length != 4 || memcmp(id, "test", 4)) return -81;
    return version == 1 && hash == 2 ? 0 : -82;
}
static int32_t extension(uint64_t session, const NativeExtensionCommand *c) {
    assert(session == 7 && c && c->reserved == 0 && c->owner_view == 1);
    assert(c->extension_id_length == 4 && !memcmp(c->extension_id, "test", 4));
    commands++;
    return 0;
}
static int32_t invalidate(uint64_t session, const NativeArtifactKey *keys, int32_t count) {
    assert(session == 7 && count == 1 && keys[0].source == 42 && keys[0].artifact == 2);
    commands++;
    return 0;
}
static void call_from_worker(void) {
    NativeClickEvent e = {0};
    assert(callbacks.click(7, 1, 0, &e) == GPUI_MOON_ERR_THREAD);
}
#if defined(_WIN32)
static DWORD WINAPI worker(LPVOID ignored) { (void)ignored; call_from_worker(); return 0; }
#else
static void *worker(void *ignored) { (void)ignored; call_from_worker(); return NULL; }
#endif

static int32_t run(uint64_t app, const ManagedCallbacks *c) {
    assert(app == 1 && c && c->struct_size == sizeof(*c));
    callbacks = *c;
    assert(c->application_started(app) == 0);
    assert(commands == 4 && menus == 1);
    assert(c->menu_applied(app, 1) == 0);
    assert(c->menu_action(app, 25) == 0);

    RenderArena arena = {0};
    uint32_t root = 999;
    uint64_t publication = 999;
    assert(c->render(7, &arena, &root, &publication) == 0);
    assert(publication == 1 && root == 0 && arena.node_length == 1);
    assert(arena.utf8_length == 5 && !memcmp(arena.utf8, "hello", 5));
    RenderArena rejected = {0};
    uint32_t rejected_root = 999;
    uint64_t rejected_id = 999;
    assert(c->render(7, &rejected, &rejected_root, &rejected_id) == GPUI_MOON_ERR_PUBLICATION);
    assert(rejected.nodes == NULL && rejected_id == 0 && rejected_root == 0);
    /* A failed competing publication must not retire the first one's memory. */
    assert(!memcmp(arena.utf8, "hello", 5));
    assert(c->render_completed(7, 99, 0) == GPUI_MOON_ERR_PUBLICATION);
    assert(c->render_completed(7, 1, 0) == 0);

    NativeClickEvent click = {1.5f, 2.5f, 3, 4};
    assert(c->click(7, 0x180000001ull, 55, &click) == 0);
    const uint8_t text[] = "event";
    NativeControlEvent control = {0};
    control.kind = 100;
    control.revision = 9;
    control.data = text;
    control.data_length = 5;
    assert(c->control_event(7, 0x180000002ull, &control) == 0);
    control.reserved = 1;
    assert(c->control_event(7, 1, &control) == GPUI_MOON_ERR_ARGUMENT);

    assert(c->list_render_range(7, 0x100000001ull, 42, 10, 1, &arena, &root, &publication) == 0);
    assert(publication == 2 && arena.node_length == 2 && arena.child_length == 1);
    assert(arena.children[0].parent == root && arena.children[0].child == 1);
    assert(c->accept_artifact(7, 43, 2) == GPUI_MOON_ERR_PUBLICATION);
    assert(c->accept_artifact(7, 42, 2) == 0);
    assert(c->release_artifact(7, 42, 2, 0) == 0);
    assert(c->release_artifact(7, 42, 2, 0) == 0);
    /* User failure after reply must roll back the buffer publication. */
    assert(c->list_render_range(7, 0x100000001ull, 43, 10, 1, &arena, &root, &publication) == -777);
    assert(arena.nodes == NULL && publication == 0);
    assert(c->dynamic_frame(7, 1) == 0);

#if defined(_WIN32)
    HANDLE thread = CreateThread(NULL, 0, worker, NULL, 0, NULL);
    assert(thread != NULL);
    assert(WaitForSingleObject(thread, INFINITE) == WAIT_OBJECT_0);
    CloseHandle(thread);
#else
    pthread_t thread;
    assert(pthread_create(&thread, NULL, worker, NULL) == 0);
    assert(pthread_join(thread, NULL) == 0);
#endif
    assert(c->window_closed(app, 7, 0) == 0);
    return 0;
}

EXPORT const GpuiDotnetApiV3 *gpui_dotnet_get_api(uint32_t version) {
    static GpuiDotnetApiV3 api;
    if (version != 8) return NULL;
    api = (GpuiDotnetApiV3){0};
    api.struct_size = (uint32_t)sizeof(api);
    api.abi_version = 8;
    api.schema_hash = GPUI_TEST_SCHEMA_HASH;
    api.validate_render = validate;
    api.run_application = run;
    api.notify_view = notify;
    api.dispatch_command = resource;
    api.dispatch_application_command = application;
    api.dispatch_application_menu = menu;
    api.supports_extension = supports;
    api.dispatch_extension_command = extension;
    api.invalidate_artifacts = invalidate;
#if FAKE_MODE == 1
    api.schema_hash++;
#elif FAKE_MODE == 2
    api.struct_size = 4;
#elif FAKE_MODE == 3
    api.notify_view = NULL;
#endif
    return &api;
}
