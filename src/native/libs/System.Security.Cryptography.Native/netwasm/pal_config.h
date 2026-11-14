#pragma once
// NetWasm: the shim builds single-threaded; emscripten's libc implements pthread_once for it.
#include <pthread.h>
