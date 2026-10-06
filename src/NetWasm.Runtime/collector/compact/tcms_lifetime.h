/* SPDX-License-Identifier: Apache-2.0. NetWasm lifetime extensions. */
#ifndef NETWASM_TCMS_LIFETIME_H
#define NETWASM_TCMS_LIFETIME_H
#include "tcms.h"

typedef void (*TcmsFinalizer)(void *object, void *state);
typedef enum { TCMS_WEAK_SHORT, TCMS_WEAK_LONG } TcmsWeakLifetime;

TcmsResult tcms_register_finalizer(Tcms *, uintptr_t object, TcmsFinalizer,
    void *state, TcmsFinalizer *previous, void **previous_state);
bool tcms_has_pending_finalizers(const Tcms *);
size_t tcms_invoke_finalizers(Tcms *);
TcmsResult tcms_register_weak(Tcms *, uintptr_t *slot, uintptr_t target, TcmsWeakLifetime);
bool tcms_unregister_weak(Tcms *, uintptr_t *slot, TcmsWeakLifetime);
TcmsResult tcms_register_ephemeron(Tcms *, uintptr_t *key, uintptr_t *value);
bool tcms_unregister_ephemeron(Tcms *, uintptr_t *key, uintptr_t *value);
#endif
