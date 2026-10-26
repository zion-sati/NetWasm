/* Native interop qualification fixture: a native archive importing a host function. */
typedef __UINT64_TYPE__ host_uint64;

__attribute__((import_module("host_ui"), import_name("create_node")))
extern host_uint64 host_create_node(unsigned int type);

host_uint64 app_create_node(unsigned int type) { return host_create_node(type); }
