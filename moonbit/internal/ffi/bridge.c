#include "bridge_internal.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
static __declspec(thread) MoonHost *active_host;
static volatile LONG run_claimed;
static int claim_run(void) { return InterlockedCompareExchange(&run_claimed, 1, 0) == 0; }
static void close_library(void *library) { if (library) FreeLibrary((HMODULE)library); }
#else
#include <dlfcn.h>
#include <stdatomic.h>
static _Thread_local MoonHost *active_host;
static atomic_flag run_claimed = ATOMIC_FLAG_INIT;
static int claim_run(void) { return !atomic_flag_test_and_set(&run_claimed); }
static void close_library(void *library) { if (library) dlclose(library); }
#endif

MoonHost *moon_active_host(void) { return active_host; }

int32_t moon_host_error(MoonHost *host, int32_t status, const char *message) {
    if (host != NULL) snprintf(host->error, sizeof(host->error), "%s", message ? message : "Native bridge error");
    return status;
}


int32_t moon_require_running(MoonHost *host) {
    if (host == NULL || !host->running || host->api == NULL) return GPUI_MOON_ERR_STATE;
    if (active_host != host) return GPUI_MOON_ERR_THREAD;
    return 0;
}

void *gpui_moon_host_new(void) { return calloc(1, sizeof(MoonHost)); }
int32_t gpui_moon_host_is_null(void *host) { return host == NULL; }

int32_t gpui_moon_host_load(void *host, const uint8_t *path, int32_t length, uint64_t schema_hash) {
    MoonHost *h = (MoonHost *)host;
    if (h == NULL || !moon_valid_bytes(path, length) || length == 0 ||
        length > 32767 || memchr(path, 0, (size_t)length) != NULL || schema_hash == 0)
        return GPUI_MOON_ERR_ARGUMENT;
    if (h->library != NULL || h->running) return GPUI_MOON_ERR_STATE;
    GpuiGetApiFn get_api = NULL;
#if defined(_WIN32)
    int needed = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, (const char *)path, length, NULL, 0);
    if (needed == 0) return moon_host_error(h, GPUI_MOON_ERR_LOAD, "Native library path is not valid UTF-8");
    wchar_t *wide = (wchar_t *)calloc((size_t)needed + 1, sizeof(wchar_t));
    if (wide == NULL) return GPUI_MOON_ERR_MEMORY;
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, (const char *)path, length, wide, needed) != needed) {
        free(wide);
        return GPUI_MOON_ERR_LOAD;
    }
    h->library = (void *)LoadLibraryW(wide);
    free(wide);
    if (h->library == NULL) {
        snprintf(h->error, sizeof(h->error), "LoadLibraryW failed (Windows error %lu)", (unsigned long)GetLastError());
        return GPUI_MOON_ERR_LOAD;
    }
    FARPROC symbol = GetProcAddress((HMODULE)h->library, "gpui_dotnet_get_api");
    _Static_assert(sizeof(symbol) == sizeof(get_api), "Windows function pointer size");
    memcpy(&get_api, &symbol, sizeof(get_api));
#else
    char *terminated = (char *)malloc((size_t)length + 1);
    if (terminated == NULL) return GPUI_MOON_ERR_MEMORY;
    memcpy(terminated, path, (size_t)length);
    terminated[length] = 0;
    h->library = dlopen(terminated, RTLD_NOW | RTLD_LOCAL);
    free(terminated);
    if (h->library == NULL) return moon_host_error(h, GPUI_MOON_ERR_LOAD, dlerror());
    void *symbol = dlsym(h->library, "gpui_dotnet_get_api");
    _Static_assert(sizeof(symbol) == sizeof(get_api), "POSIX dlsym function pointer size");
    memcpy(&get_api, &symbol, sizeof(get_api));
#endif
    int32_t status = 0;
    if (get_api == NULL) {
        status = moon_host_error(h, GPUI_MOON_ERR_SYMBOL, "Missing gpui_dotnet_get_api");
    } else {
        const GpuiDotnetApiV3 *api = get_api(GPUI_NATIVE_ABI_VERSION);
        // Never inspect the tail before checking the fixed prefix size.
        if (api == NULL || api->struct_size < sizeof(GpuiDotnetApiV3)) {
            status = moon_host_error(h, GPUI_MOON_ERR_ABI, "Unsupported native ABI version or API-table prefix");
        } else if (api->abi_version != GPUI_NATIVE_ABI_VERSION) {
            status = moon_host_error(h, GPUI_MOON_ERR_ABI, "Native ABI version mismatch");
        } else if (api->schema_hash != schema_hash) {
            status = moon_host_error(h, GPUI_MOON_ERR_SCHEMA, "Semantic schema mismatch; rebuild both frontends and the native host");
        } else if (!api->validate_render || !api->run_application || !api->notify_view ||
                   !api->dispatch_command || !api->dispatch_application_command ||
                   !api->dispatch_application_menu || !api->supports_extension ||
                   !api->dispatch_extension_command || !api->invalidate_artifacts) {
            status = moon_host_error(h, GPUI_MOON_ERR_ABI, "Native API table has missing required entries");
        } else {
            h->api = api;
        }
    }
    if (status != 0) {
        close_library(h->library);
        h->library = NULL;
    }
    return status;
}

int32_t gpui_moon_host_error_length(void *host) {
    MoonHost *h = (MoonHost *)host;
    return h == NULL ? 0 : (int32_t)strlen(h->error);
}

int32_t gpui_moon_host_error_copy(void *host, uint8_t *buffer, int32_t length) {
    MoonHost *h = (MoonHost *)host;
    if (h == NULL || !moon_valid_bytes(buffer, length) || (size_t)length != strlen(h->error))
        return GPUI_MOON_ERR_ARGUMENT;
    if (length) memcpy(buffer, h->error, (size_t)length);
    return 0;
}

void gpui_moon_host_free(void *host) {
    MoonHost *h = (MoonHost *)host;
    if (h == NULL || h->running) return;
    moon_frames_clear(h, 0);
    // GPUI/Rust may leave process-wide registrations or worker code alive after run returns.
    // Once entered, keep the code image mapped until process exit; do not implement hot unload.
    if (!h->entered_native) close_library(h->library);
    free(h);
}

static int32_t callback_ready(void) {
    MoonHost *h = active_host;
    if (h == NULL || !h->running || h->dispatch == NULL) return GPUI_MOON_ERR_THREAD;
    if (h->callback_depth != 0) return GPUI_MOON_ERR_CALLBACK;
    return 0;
}

static int32_t dispatch(MoonRequest *request, int32_t kind, uint64_t session,
                        uint64_t a, uint64_t b, uint32_t x, uint32_t y, int32_t status) {
    int32_t ready = callback_ready();
    if (ready != 0) return ready;
    MoonHost *h = active_host;
    request->magic = GPUI_MOON_REQUEST_MAGIC;
    request->host = h;
    request->kind = kind;
    request->session = session;
    request->a = a;
    request->b = b;
    h->callback_depth = 1;
    int32_t result = h->dispatch(kind, session, a, b, x, y, status, request);
    h->callback_depth = 0;
    request->magic = 0;
    return result;
}

static int32_t output_callback(int32_t kind, uint64_t session, uint64_t a, uint64_t b,
                               uint32_t start, uint32_t count, RenderArena *arena,
                               uint32_t *root, uint64_t *publication) {
    if (arena == NULL || root == NULL || publication == NULL) return GPUI_MOON_ERR_ARGUMENT;
    memset(arena, 0, sizeof(*arena));
    *root = 0;
    *publication = 0;
    MoonRequest request = {0};
    request.arena = arena;
    request.root = root;
    request.publication = publication;
    int32_t result = dispatch(&request, kind, session, a, b, start, count, 0);
    if (result == 0 && request.published == NULL) result = GPUI_MOON_ERR_PUBLICATION;
    if (result != 0) {
        if (request.published != NULL)
            (void)moon_frame_retire(request.host, kind, session, kind == GPUI_MOON_CB_RANGE ? b : 0, *publication, 0);
        memset(arena, 0, sizeof(*arena));
        *root = 0;
        *publication = 0;
    }
    return result;
}

static int32_t render(uint64_t session, RenderArena *arena, uint32_t *root, uint64_t *revision) {
    return output_callback(GPUI_MOON_CB_RENDER, session, 0, 0, 0, 0, arena, root, revision);
}

static int32_t range(uint64_t session, uint64_t renderer, uint64_t source, uint32_t start,
                     uint32_t count, RenderArena *arena, uint32_t *root, uint64_t *artifact) {
    if (source == 0 || count > 512 || start > UINT32_MAX - count) return GPUI_MOON_ERR_ARGUMENT;
    return output_callback(GPUI_MOON_CB_RANGE, session, renderer, source, start, count, arena, root, artifact);
}

static int32_t completed(uint64_t session, uint64_t revision, int32_t status) {
    int32_t ready = callback_ready();
    if (ready != 0) return ready;
    int32_t retired = moon_frame_retire(active_host, GPUI_MOON_CB_RENDER, session, 0, revision, 1);
    if (retired != 0) return retired;
    MoonRequest request = {0};
    return dispatch(&request, GPUI_MOON_CB_RENDER_COMPLETED, session, revision, 0, 0, 0, status);
}

static int32_t accept(uint64_t session, uint64_t source, uint64_t artifact) {
    int32_t ready = callback_ready();
    if (ready != 0) return ready;
    int32_t retired = moon_frame_retire(active_host, GPUI_MOON_CB_RANGE, session, source, artifact, 1);
    if (retired != 0) return retired;
    MoonRequest request = {0};
    return dispatch(&request, GPUI_MOON_CB_ACCEPT_ARTIFACT, session, source, artifact, 0, 0, 0);
}

static int32_t release(uint64_t session, uint64_t source, uint64_t artifact, int32_t status) {
    int32_t ready = callback_ready();
    if (ready != 0) return ready;
    int32_t retired = moon_frame_retire(active_host, GPUI_MOON_CB_RANGE, session, source, artifact, 0);
    if (retired != 0) return retired;
    MoonRequest request = {0};
    return dispatch(&request, GPUI_MOON_CB_RELEASE_ARTIFACT, session, source, artifact, 0, 0, status);
}

static int32_t click(uint64_t session, uint64_t token, uint64_t payload, const NativeClickEvent *event) {
    if (event == NULL) return GPUI_MOON_ERR_ARGUMENT;
    MoonRequest request = {0};
    request.click = event;
    return dispatch(&request, GPUI_MOON_CB_CLICK, session, token, payload, 0, 0, 0);
}

static int32_t control(uint64_t session, uint64_t token, const NativeControlEvent *event) {
    if (event == NULL || event->reserved != 0 || event->reserved2 != 0 ||
        !moon_valid_bytes(event->data, event->data_length)) return GPUI_MOON_ERR_ARGUMENT;
    MoonRequest request = {0};
    request.control = event;
    return dispatch(&request, GPUI_MOON_CB_CONTROL, session, token, event->revision, event->kind, event->flags, 0);
}

static int32_t started(uint64_t application) {
    MoonRequest request = {0};
    return dispatch(&request, GPUI_MOON_CB_STARTED, application, 0, 0, 0, 0, 0);
}

static int32_t closed(uint64_t application, uint64_t session, int32_t status) {
    int32_t ready = callback_ready();
    if (ready != 0) return ready;
    moon_frames_clear(active_host, session);
    MoonRequest request = {0};
    return dispatch(&request, GPUI_MOON_CB_WINDOW_CLOSED, application, session, 0, 0, 0, status);
}

static int32_t dynamic(uint64_t session, uint32_t owner) {
    MoonRequest request = {0};
    return dispatch(&request, GPUI_MOON_CB_DYNAMIC, session, owner, 0, 0, 0, 0);
}

static int32_t menu_action(uint64_t application, uint64_t action) {
    MoonRequest request = {0};
    return dispatch(&request, GPUI_MOON_CB_MENU_ACTION, application, action, 0, 0, 0, 0);
}

static int32_t menu_applied(uint64_t application, uint64_t generation) {
    MoonRequest request = {0};
    return dispatch(&request, GPUI_MOON_CB_MENU_APPLIED, application, generation, 0, 0, 0, 0);
}

int32_t gpui_moon_host_run(void *host, uint64_t application, GpuiMoonDispatch callback) {
    MoonHost *h = (MoonHost *)host;
    if (h == NULL || h->api == NULL || callback == NULL || application == 0) return GPUI_MOON_ERR_ARGUMENT;
    if (h->running || active_host != NULL || !claim_run()) return GPUI_MOON_ERR_STATE;
    ManagedCallbacks callbacks = {0};
    callbacks.struct_size = (uint32_t)sizeof(callbacks);
    callbacks.render = render;
    callbacks.click = click;
    callbacks.list_render_range = range;
    callbacks.control_event = control;
    callbacks.application_started = started;
    callbacks.window_closed = closed;
    callbacks.menu_action = menu_action;
    callbacks.dynamic_frame = dynamic;
    callbacks.render_completed = completed;
    callbacks.release_artifact = release;
    callbacks.accept_artifact = accept;
    callbacks.menu_applied = menu_applied;
    h->dispatch = callback;
    h->application = application;
    h->running = h->entered_native = 1;
    active_host = h;
    int32_t status = h->api->run_application(application, &callbacks);
    active_host = NULL;
    h->running = 0;
    h->dispatch = NULL;
    moon_frames_clear(h, 0);
    return status;
}

static MoonRequest *event_request(void *request) {
    MoonRequest *r = (MoonRequest *)request;
    return r != NULL && r->magic == GPUI_MOON_REQUEST_MAGIC && r->host == active_host ? r : NULL;
}

float gpui_moon_event_x(void *request) {
    MoonRequest *r = event_request(request);
    return r && r->click ? r->click->x : 0.0f;
}
float gpui_moon_event_y(void *request) {
    MoonRequest *r = event_request(request);
    return r && r->click ? r->click->y : 0.0f;
}
uint32_t gpui_moon_event_buttons(void *request) {
    MoonRequest *r = event_request(request);
    return r && r->click ? r->click->buttons : 0;
}
uint32_t gpui_moon_event_modifiers(void *request) {
    MoonRequest *r = event_request(request);
    return r && r->click ? r->click->modifiers : 0;
}
int32_t gpui_moon_event_data_length(void *request) {
    MoonRequest *r = event_request(request);
    return r && r->control ? r->control->data_length : GPUI_MOON_ERR_ARGUMENT;
}
int32_t gpui_moon_event_data_copy(void *request, uint8_t *buffer, int32_t length) {
    MoonRequest *r = event_request(request);
    if (r == NULL || r->control == NULL || !moon_valid_bytes(buffer, length) ||
        length != r->control->data_length) return GPUI_MOON_ERR_ARGUMENT;
    if (length) memcpy(buffer, r->control->data, (size_t)length);
    return 0;
}
