#include "bridge_internal.h"
#include <limits.h>
#include <stdlib.h>
#include <string.h>

uint16_t moon_read_u16(const uint8_t *p) {
    return (uint16_t)((uint16_t)p[0] | ((uint16_t)p[1] << 8));
}


uint32_t moon_read_u32(const uint8_t *p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) |
           ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

uint64_t moon_read_u64(const uint8_t *p) {
    return (uint64_t)moon_read_u32(p) | ((uint64_t)moon_read_u32(p + 4) << 32);
}

int32_t moon_valid_bytes(const uint8_t *bytes, int32_t length) {
    return length >= 0 && (uint32_t)length <= GPUI_MOON_MAX_PACKET &&
           (length == 0 || bytes != NULL);
}

void moon_frame_free(MoonFrame *frame) {
    if (frame == NULL) return;
    free(frame->arena.nodes);
    free(frame->arena.ops);
    free(frame->arena.children);
    free(frame->arena.utf8);
    free(frame);
}

int32_t moon_frame_decode(const uint8_t *packet, int32_t length, MoonFrame **result) {
    if (result == NULL) return GPUI_MOON_ERR_ARGUMENT;
    *result = NULL;
    if (!moon_valid_bytes(packet, length) || length < GPUI_MOON_PACKET_HEADER_SIZE)
        return GPUI_MOON_ERR_PACKET;
    if (moon_read_u32(packet) != GPUI_MOON_PACKET_MAGIC ||
        moon_read_u32(packet + 24) != 0 || moon_read_u32(packet + 28) != 0)
        return GPUI_MOON_ERR_PACKET;
    const uint32_t nodes = moon_read_u32(packet + 4);
    const uint32_t ops = moon_read_u32(packet + 8);
    const uint32_t children = moon_read_u32(packet + 12);
    const uint32_t utf8 = moon_read_u32(packet + 16);
    const uint32_t root = moon_read_u32(packet + 20);
    if (nodes == 0 || root >= nodes || nodes > GPUI_MOON_MAX_RECORDS ||
        ops > GPUI_MOON_MAX_RECORDS || children > GPUI_MOON_MAX_RECORDS)
        return GPUI_MOON_ERR_LIMIT;
    const uint64_t expected = 32ull + 12ull * nodes + 24ull * ops + 8ull * children + utf8;
    if (expected != (uint64_t)length) return GPUI_MOON_ERR_PACKET;
    MoonFrame *frame = (MoonFrame *)calloc(1, sizeof(MoonFrame));
    if (frame == NULL) return GPUI_MOON_ERR_MEMORY;
    frame->arena.nodes = (NodeRecord *)calloc(nodes, sizeof(NodeRecord));
    frame->arena.ops = ops ? (OpRecord *)calloc(ops, sizeof(OpRecord)) : NULL;
    frame->arena.children = children ? (ChildRecord *)calloc(children, sizeof(ChildRecord)) : NULL;
    frame->arena.utf8 = utf8 ? (uint8_t *)malloc(utf8) : NULL;
    if (frame->arena.nodes == NULL || (ops && frame->arena.ops == NULL) ||
        (children && frame->arena.children == NULL) || (utf8 && frame->arena.utf8 == NULL)) {
        moon_frame_free(frame);
        return GPUI_MOON_ERR_MEMORY;
    }
    frame->arena.node_length = frame->arena.node_capacity = (int32_t)nodes;
    frame->arena.op_length = frame->arena.op_capacity = (int32_t)ops;
    frame->arena.child_length = frame->arena.child_capacity = (int32_t)children;
    frame->arena.utf8_length = frame->arena.utf8_capacity = (int32_t)utf8;
    frame->arena.generation = 1;
    frame->root = root;
    const uint8_t *p = packet + 32;
    for (uint32_t i = 0; i < nodes; i++, p += 12) {
        NodeRecord *node = &frame->arena.nodes[i];
        node->component = moon_read_u16(p);
        node->flags = moon_read_u16(p + 2);
        node->data_offset = moon_read_u32(p + 4);
        node->data_length = moon_read_u32(p + 8);
    }
    for (uint32_t i = 0; i < ops; i++, p += 24) {
        OpRecord *op = &frame->arena.ops[i];
        op->node = moon_read_u32(p);
        op->code = moon_read_u16(p + 4);
        op->value_kind = moon_read_u16(p + 6);
        op->a = moon_read_u64(p + 8);
        op->b = moon_read_u64(p + 16);
    }
    for (uint32_t i = 0; i < children; i++, p += 8) {
        frame->arena.children[i].parent = moon_read_u32(p);
        frame->arena.children[i].child = moon_read_u32(p + 4);
    }
    if (utf8 != 0) memcpy(frame->arena.utf8, p, utf8);
    *result = frame;
    return 0;
}

void moon_frames_clear(MoonHost *host, uint64_t session) {
    MoonFrame **slot = &host->pending;
    while (*slot != NULL) {
        MoonFrame *frame = *slot;
        if (session == 0 || frame->session == session) {
            *slot = frame->next;
            moon_frame_free(frame);
        } else {
            slot = &frame->next;
        }
    }
}

int32_t moon_frame_retire(MoonHost *host, int32_t kind, uint64_t session,
                          uint64_t source, uint64_t publication, int required) {
    MoonFrame **slot = &host->pending;
    while (*slot != NULL) {
        MoonFrame *frame = *slot;
        if (frame->kind == kind && frame->session == session && frame->publication == publication) {
            if (frame->source != source) return GPUI_MOON_ERR_PUBLICATION;
            *slot = frame->next;
            moon_frame_free(frame);
            return 0;
        }
        slot = &frame->next;
    }
    return required ? GPUI_MOON_ERR_PUBLICATION : 0;
}

int32_t gpui_moon_reply(void *request, const uint8_t *packet, int32_t length, uint64_t publication) {
    MoonRequest *r = (MoonRequest *)request;
    if (r == NULL || r->magic != GPUI_MOON_REQUEST_MAGIC ||
        r->host != moon_active_host() || publication == 0 || r->published != NULL ||
        (r->kind != GPUI_MOON_CB_RENDER && r->kind != GPUI_MOON_CB_RANGE))
        return GPUI_MOON_ERR_PUBLICATION;
    for (MoonFrame *p = r->host->pending; p != NULL; p = p->next) {
        if (p->kind == r->kind && p->session == r->session &&
            (r->kind == GPUI_MOON_CB_RENDER || p->publication == publication))
            return GPUI_MOON_ERR_PUBLICATION;
    }
    MoonFrame *frame = NULL;
    int32_t status = moon_frame_decode(packet, length, &frame);
    if (status != 0) return status;
    status = r->host->api->validate_render(&frame->arena, frame->root);
    if (status != 0) {
        moon_frame_free(frame);
        return status;
    }
    frame->kind = r->kind;
    frame->session = r->session;
    frame->source = r->kind == GPUI_MOON_CB_RANGE ? r->b : 0;
    frame->publication = publication;
    frame->next = r->host->pending;
    r->host->pending = frame;
    r->published = frame;
    *r->arena = frame->arena;
    *r->root = frame->root;
    *r->publication = publication;
    return 0;
}
