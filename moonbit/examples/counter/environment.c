#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#if defined(_MSC_VER)
#pragma comment(linker, "/manifestdependency:\"type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'\"")
#endif

int32_t gpui_example_path_length(void) {
    const char *path = getenv("GPUI_MOON_NATIVE");
    if (!path || strlen(path) > 32767) return -1;
    return (int32_t)strlen(path);
}
int32_t gpui_example_path_copy(uint8_t *out, int32_t length) {
    const char *path = getenv("GPUI_MOON_NATIVE");
    if (!path || !out || length < 0 || (size_t)length != strlen(path)) return -1;
    memcpy(out, path, (size_t)length);
    return 0;
}
void gpui_example_exit(int32_t code) { exit(code); }
