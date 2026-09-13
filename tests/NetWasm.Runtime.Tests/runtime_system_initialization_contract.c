#include <assert.h>
#include "runtime_system_initializer.h"
#include "runtime_environment_reader.h"

static unsigned libc_initializations;
static unsigned environment_reads;
static char *environment[] = {"APP_MODE=unit", 0};
extern char **__environ;

char **runtime_read_environment(void) {
    assert(libc_initializations == 1);
    ++environment_reads;
    return environment;
}

void runtime_initialize_libc(void) {
    assert(environment_reads == 0);
    ++libc_initializations;
}

int main(void) {
    assert(libc_initializations == 0);
    runtime_initialize_system();
    assert(libc_initializations == 1);
    assert(environment_reads == 1);
    assert(__environ == environment);
    return 0;
}
