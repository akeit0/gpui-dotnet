#include "bridge_internal.h"
#include <stdlib.h>
#include <string.h>

static int valid_u16(int32_t value) { return value >= 0 && value <= UINT16_MAX; }

int32_t gpui_moon_notify(void *host, uint64_t session) {
    MoonHost *h = (MoonHost *)host;
    int32_t status = moon_require_running(h);
    if (status != 0) return status;
    return session == 0 ? GPUI_MOON_ERR_ARGUMENT : h->api->notify_view(session);
}


int32_t gpui_moon_application_command(void *host, uint64_t application, uint64_t window,
    int32_t command, int32_t flags, const uint8_t *data, int32_t length,
    float left, float top, float width, float height) {
    MoonHost *h = (MoonHost *)host;
    int32_t status = moon_require_running(h);
    if (status != 0) return status;
    if (application != h->application || !valid_u16(command) || !valid_u16(flags) ||
        !moon_valid_bytes(data, length)) return GPUI_MOON_ERR_ARGUMENT;
    NativeApplicationCommand value = {0};
    value.window_id = window;
    value.command = (uint16_t)command;
    value.flags = (uint16_t)flags;
    value.title = data;
    value.title_length = length;
    value.left = left;
    value.top = top;
    value.width = width;
    value.height = height;
    return h->api->dispatch_application_command(application, &value);
}

int32_t gpui_moon_resource_command(void *host, uint64_t session, uint32_t owner,
    int32_t resource, int32_t command, const uint8_t *key, int32_t key_length,
    const uint8_t *data, int32_t data_length, uint64_t a, uint64_t b) {
    MoonHost *h = (MoonHost *)host;
    int32_t status = moon_require_running(h);
    if (status != 0) return status;
    if (session == 0 || owner == 0 || !valid_u16(resource) || !valid_u16(command) ||
        !moon_valid_bytes(key, key_length) || !moon_valid_bytes(data, data_length))
        return GPUI_MOON_ERR_ARGUMENT;
    NativeResourceCommand value = {0};
    value.owner_view = owner;
    value.resource_kind = (uint16_t)resource;
    value.command = (uint16_t)command;
    value.key = key;
    value.key_length = key_length;
    value.data = data;
    value.data_length = data_length;
    value.a = a;
    value.b = b;
    return h->api->dispatch_command(session, &value);
}

int32_t gpui_moon_supports_extension(void *host, const uint8_t *id, int32_t length,
                                    uint32_t version, uint64_t schema_hash) {
    MoonHost *h = (MoonHost *)host;
    if (h == NULL || h->api == NULL) return GPUI_MOON_ERR_STATE;
    if (h->running && moon_active_host() != h) return GPUI_MOON_ERR_THREAD;
    if (!moon_valid_bytes(id, length)) return GPUI_MOON_ERR_ARGUMENT;
    return h->api->supports_extension(id, length, version, schema_hash);
}

int32_t gpui_moon_extension_command(void *host, uint64_t session, uint32_t owner,
    int32_t command, int32_t flags, uint32_t version, uint64_t schema_hash, uint64_t revision,
    const uint8_t *extension, int32_t extension_length, const uint8_t *component, int32_t component_length,
    const uint8_t *key, int32_t key_length, const uint8_t *data, int32_t data_length) {
    MoonHost *h = (MoonHost *)host;
    int32_t status = moon_require_running(h);
    if (status != 0) return status;
    if (session == 0 || owner == 0 || !valid_u16(command) || !valid_u16(flags) ||
        !moon_valid_bytes(extension, extension_length) || !moon_valid_bytes(component, component_length) ||
        !moon_valid_bytes(key, key_length) || !moon_valid_bytes(data, data_length))
        return GPUI_MOON_ERR_ARGUMENT;
    NativeExtensionCommand value = {0};
    value.owner_view = owner;
    value.command = (uint16_t)command;
    value.flags = (uint16_t)flags;
    value.schema_version = version;
    value.schema_hash = schema_hash;
    value.expected_revision = revision;
    value.extension_id = extension;
    value.extension_id_length = extension_length;
    value.component_kind = component;
    value.component_kind_length = component_length;
    value.key = key;
    value.key_length = key_length;
    value.payload = data;
    value.payload_length = data_length;
    return h->api->dispatch_extension_command(session, &value);
}

int32_t gpui_moon_invalidate_artifacts(void *host, uint64_t session, const uint8_t *keys, int32_t length) {
    MoonHost *h = (MoonHost *)host;
    int32_t status = moon_require_running(h);
    if (status != 0) return status;
    if (!moon_valid_bytes(keys, length) || length <= 0 || length % 16 != 0 || length / 16 > 65536)
        return GPUI_MOON_ERR_ARGUMENT;
    const int32_t count = length / 16;
    NativeArtifactKey *values = (NativeArtifactKey *)calloc((size_t)count, sizeof(*values));
    if (values == NULL) return GPUI_MOON_ERR_MEMORY;
    for (int32_t i = 0; i < count; i++) {
        values[i].source = moon_read_u64(keys + 16 * i);
        values[i].artifact = moon_read_u64(keys + 16 * i + 8);
    }
    status = h->api->invalidate_artifacts(session, values, count);
    free(values);
    return status;
}

int32_t gpui_moon_menu(void *host, uint64_t application, uint64_t generation,
                       const uint8_t *packet, int32_t length) {
    MoonHost *h = (MoonHost *)host;
    int32_t status = moon_require_running(h);
    if (status != 0) return status;
    if (application != h->application || generation == 0 || !moon_valid_bytes(packet, length) || length < 4)
        return GPUI_MOON_ERR_ARGUMENT;
    const uint32_t count = moon_read_u32(packet);
    const uint64_t offset = 4ull + 24ull * count;
    if (count > 4096 || offset > (uint64_t)length || (uint64_t)length - offset > 1024u * 1024u)
        return GPUI_MOON_ERR_LIMIT;
    const uint32_t data_length = (uint32_t)((uint64_t)length - offset);
    NativeMenuRecord *items = count ? (NativeMenuRecord *)calloc(count, sizeof(*items)) : NULL;
    if (count && items == NULL) return GPUI_MOON_ERR_MEMORY;
    for (uint32_t i = 0; i < count; i++) {
        const uint8_t *p = packet + 4 + 24 * i;
        uint32_t begin = moon_read_u32(p + 16), size = moon_read_u32(p + 20);
        if (begin > data_length || size > data_length - begin) {
            free(items);
            return GPUI_MOON_ERR_PACKET;
        }
        items[i].parent = moon_read_u32(p);
        items[i].kind = moon_read_u16(p + 4);
        items[i].flags = moon_read_u16(p + 6);
        items[i].action_id = moon_read_u64(p + 8);
        items[i].title = packet + (size_t)offset + begin;
        items[i].title_length = (int32_t)size;
    }
    NativeMenuCommand value = {0};
    value.items = items;
    value.item_length = (int32_t)count;
    value.generation = generation;
    status = h->api->dispatch_application_menu(application, &value);
    free(items);
    return status;
}
