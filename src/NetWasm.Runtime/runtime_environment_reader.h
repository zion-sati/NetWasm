#ifndef NETWASM_RUNTIME_ENVIRONMENT_READER_H
#define NETWASM_RUNTIME_ENVIRONMENT_READER_H

/* The returned table and strings share one C allocation, owned by the caller. */
char **runtime_read_environment(void);

#endif
