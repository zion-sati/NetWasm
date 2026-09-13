#ifndef NETWASM_COLLECTOR_LIFECYCLE_H
#define NETWASM_COLLECTOR_LIFECYCLE_H

void collector_initialize(void);
void collector_enable_interior_pointers(void);
void collector_disable_stack_scanning(void);
void collector_enable_on_demand_finalization(void);
void collector_enable_java_finalization_order(void);

#endif
