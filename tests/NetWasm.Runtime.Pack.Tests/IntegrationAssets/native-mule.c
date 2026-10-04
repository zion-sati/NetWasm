/* Native interop qualification fixture, compiled by the pinned maintainer toolchain. */
typedef __UINTPTR_TYPE__ mule_uintptr;
typedef __INTPTR_TYPE__ mule_intptr;
typedef __INT64_TYPE__ mule_int64;
typedef __UINT64_TYPE__ mule_uint64;

static volatile unsigned char initialized_data[2 * 1024 * 1024] = { 0x42 };
static unsigned int initialization_count;

__attribute__((constructor)) static void initialize_mule(void)
{
    initialization_count++;
    initialized_data[sizeof(initialized_data) - 1] = 0x71;
}

int native_mule_probe(void)
{
    return initialized_data[0] + initialized_data[sizeof(initialized_data) - 1] + initialization_count;
}

mule_uintptr native_mule_data_end(void)
{
    return (mule_uintptr)&initialized_data[sizeof(initialized_data)];
}

int native_mule_sum(int left, int right) { return left + right; }
unsigned int native_mule_unsigned32(unsigned int value) { return value; }
mule_intptr native_mule_signed_pointer(mule_intptr value) { return value; }
mule_int64 native_mule_signed(mule_int64 value) { return value; }
mule_uint64 native_mule_unsigned(mule_uint64 value) { return value; }
float native_mule_single(float value) { return value; }
double native_mule_double(double value) { return value; }
mule_uintptr native_mule_pointer(mule_uintptr value) { return value; }

typedef int (*mule_binary_callback)(int left, int right);
typedef mule_intptr (*mule_token_callback)(mule_intptr token, int delta);
typedef int (*mule_i32_callback)(int value);
typedef unsigned int (*mule_u32_callback)(unsigned int value);
typedef mule_int64 (*mule_i64_callback)(mule_int64 value);
typedef mule_uint64 (*mule_u64_callback)(mule_uint64 value);
typedef float (*mule_f32_callback)(float value);
typedef double (*mule_f64_callback)(double value);
typedef mule_intptr (*mule_intptr_callback)(mule_intptr value);
typedef mule_uintptr (*mule_uintptr_callback)(mule_uintptr value);
typedef unsigned char *(*mule_pointer_callback)(unsigned char *value);
typedef void (*mule_void_callback)(int value);
typedef int (*mule_read_callback)(void);
typedef double (*mule_mixed_callback)(int integer, double real, mule_intptr pointer, mule_int64 wide);
static int callback_scalar_invocation_count;

int native_mule_callback_scalar_matrix(
    mule_i32_callback signed32,
    mule_u32_callback unsigned32,
    mule_i64_callback signed64,
    mule_u64_callback unsigned64,
    mule_f32_callback single,
    mule_f64_callback real,
    mule_intptr_callback signed_pointer,
    mule_uintptr_callback unsigned_pointer,
    mule_pointer_callback pointer,
    mule_void_callback mark,
    mule_read_callback read_marker,
    mule_mixed_callback mixed)
{
    const int marker = 0x1234567 + ++callback_scalar_invocation_count;
    const mule_intptr signed_pointer_value = sizeof(mule_intptr) == 8
        ? (mule_intptr)0x123456789abcdefLL
        : (mule_intptr)(mule_uintptr)0x81234567U;
    const mule_uintptr unsigned_pointer_value = sizeof(mule_uintptr) == 8
        ? (mule_uintptr)0xf123456789abcde0ULL
        : (mule_uintptr)0xf1234560U;
    unsigned char bytes[] = { 11, 22, 33, 44, 55, 66, 77, 88 };

    if (signed32(-2000000000) != -1999999993) return -20;
    if (unsigned32(0xf1234567U) != (0xf1234567U ^ 0xa5a5a5a5U)) return -21;
    if (signed64(-0x123456789abcdefLL) != -0x123456789abcdf8LL) return -22;
    if (unsigned64(0xf123456789abcdefULL) !=
        (0xf123456789abcdefULL ^ 0xa5a5a5a5a5a5a5a5ULL)) return -23;
    if (single(1.5F) != -3.0F) return -24;
    if (real(1.0000000000000002) != 1.5000000000000002) return -25;
    if (signed_pointer(signed_pointer_value) != (signed_pointer_value ^ 0x55)) return -26;
    if (unsigned_pointer(unsigned_pointer_value) != unsigned_pointer_value + 3) return -27;
    if (pointer(bytes) != bytes + 5 || bytes[0] != 11 || bytes[3] != 44 ||
        bytes[4] != (unsigned char)(55 ^ 0x5a) || bytes[5] != 66 || bytes[7] != 88)
        return -28;
    if (pointer(0) != 0) return -29;
    mark(marker);
    if (read_marker() != marker) return -30;
    if (mixed(3, 2.5, signed_pointer_value,
        (mule_int64)-0x123456789abcdefLL) != 3328.5) return -31;
    return 42;
}

int native_mule_invoke_binary(mule_binary_callback callback, int left, int right)
{
    return callback(left, right);
}

mule_intptr native_mule_invoke_token(mule_token_callback callback, mule_intptr token, int delta)
{
    return callback(token, delta);
}

int native_mule_invoke_callbacks(
    mule_token_callback first,
    mule_token_callback second,
    mule_intptr token,
    int input,
    mule_intptr *first_result,
    mule_intptr *second_result)
{
    if (first == 0 || second == 0 || first == second ||
        first_result == 0 || second_result == 0)
        return -7;
    *first_result = first(token, input);
    *second_result = second(token, input);
    return 42;
}

int native_mule_invoke_dynamic(mule_i32_callback callback, int input)
{
    return callback(input);
}

static mule_token_callback retained_first;
static mule_token_callback retained_second;
static mule_intptr retained_token;
static unsigned int retained_register_count;
static unsigned int retained_unregister_count;

void native_mule_callback_registry_reset(void)
{
    retained_first = 0;
    retained_second = 0;
    retained_token = 0;
    retained_register_count = 0;
    retained_unregister_count = 0;
}

int native_mule_callback_register(
    mule_token_callback first,
    mule_token_callback second,
    mule_intptr token)
{
    if (first == 0 || second == 0 || first == second ||
        retained_first != 0 || retained_second != 0)
        return -7;
    retained_first = first;
    retained_second = second;
    retained_token = token;
    retained_register_count++;
    return 42;
}

int native_mule_callback_invoke(
    int input,
    mule_intptr *first_result,
    mule_intptr *second_result)
{
    if (retained_first == 0 || retained_second == 0 ||
        first_result == 0 || second_result == 0)
        return -8;
    mule_token_callback first = retained_first;
    mule_token_callback second = retained_second;
    mule_intptr token = retained_token;
    *first_result = first(token, input);
    *second_result = second(token, input);
    return 42;
}

int native_mule_callback_unregister(void)
{
    if (retained_first == 0 || retained_second == 0)
        return -8;
    retained_first = 0;
    retained_second = 0;
    retained_token = 0;
    retained_unregister_count++;
    return 42;
}

int native_mule_callback_register_count(void) { return (int)retained_register_count; }
int native_mule_callback_unregister_count(void) { return (int)retained_unregister_count; }

static unsigned int native_mule_handle_borrows;
static unsigned int native_mule_handle_releases;

void native_mule_handle_reset(void)
{
    native_mule_handle_borrows = 0;
    native_mule_handle_releases = 0;
}

int native_mule_handle_borrow(mule_uintptr handle)
{
    native_mule_handle_borrows++;
    return handle == 0x1234 ? 42 : -7;
}

int native_mule_handle_release(mule_uintptr handle)
{
    if (handle != 0x1234) return -7;
    native_mule_handle_releases++;
    return 42;
}

int native_mule_handle_borrow_count(void) { return (int)native_mule_handle_borrows; }
int native_mule_handle_release_count(void) { return (int)native_mule_handle_releases; }

int native_mule_buffer(const unsigned char *bytes, mule_uintptr count)
{
    int checksum = 0;
    for (mule_uintptr index = 0; index < count; index++)
        checksum += bytes[index];
    return checksum;
}

int native_mule_array_input_bytes(
    const unsigned char *values,
    mule_uintptr count,
    mule_uintptr capacity)
{
    if (count > capacity || (values == 0 && count != 0)) return -7;
    int total = 0;
    for (mule_uintptr index = 0; index < count; index++) total += values[index];
    return total;
}

int native_mule_array_input_ints(
    const int *values,
    mule_uintptr count,
    mule_uintptr capacity)
{
    if (count > capacity || (values == 0 && count != 0)) return -7;
    int total = 0;
    for (mule_uintptr index = 0; index < count; index++) total += values[index];
    return total;
}

int native_mule_array_output_ints(
    int *values,
    mule_uintptr count,
    mule_uintptr capacity)
{
    if (count > capacity || (values == 0 && count != 0)) return -7;
    const int output[] = { 10, 20, 12 };
    if (count > 3) return -8;
    for (mule_uintptr index = 0; index < count; index++) values[index] = output[index];
    return 42;
}

int native_mule_utf8_profile(
    const unsigned char *value,
    mule_uintptr byte_count,
    int scenario)
{
    static const unsigned char non_ascii[] = { 'M', 0xc3, 0xbc, 'l', 'e', ' ', 0xf0, 0x9f, 0xa6, 0x80 };
    static const unsigned char embedded_nul[] = { 'A', 0, 'B' };
    if (scenario == 0)
        return byte_count == 0 && (value == 0 || value[0] == 0) ? 42 : -7;
    if (value == 0 || value[byte_count] != 0) return -8;
    if (scenario == 1)
    {
        if (byte_count != sizeof(non_ascii)) return -9;
        for (mule_uintptr index = 0; index < byte_count; index++)
            if (value[index] != non_ascii[index]) return -10;
        return 42;
    }
    if (scenario == 2)
    {
        if (byte_count != sizeof(embedded_nul)) return -11;
        for (mule_uintptr index = 0; index < byte_count; index++)
            if (value[index] != embedded_nul[index]) return -12;
        return 42;
    }
    if (scenario == 3)
    {
        if (byte_count != 300) return -13;
        for (mule_uintptr index = 0; index < byte_count; index++)
            if (value[index] != 'x') return -14;
        return 42;
    }
    return -15;
}

void native_mule_ref(int *value) { *value += 7; }
void native_mule_out(mule_int64 *value) { *value = 0x123456789abcdef0LL; }

int native_mule_status(int value, int *result)
{
    if (value < 0) return -7;
    *result = value + 1;
    return 0;
}

/* Neither these bytes nor their accessor may survive an unreferenced section. */
static volatile unsigned char unused_data[1024 * 1024] = { 0x23 };
int native_mule_unused(void) { return unused_data[0]; }

/* Ordinary by-value C signatures. These functions also serve the producer ABI oracle. */
typedef struct { int value; } mule_single_int;
typedef struct { double value; } mule_single_double;
typedef struct { mule_uintptr value; } mule_single_pointer;
typedef struct { mule_single_int inner; } mule_nested_single;
typedef struct { int integer; double real; } mule_pair;
typedef struct { mule_pair pair; mule_uintptr pointer; } mule_nested;
typedef union { unsigned int integer; float real; } mule_union_scalar;
typedef union { mule_pair pair; double real; } mule_union_pair;
typedef struct __attribute__((packed)) { unsigned char marker; int value; } mule_packed;
typedef struct {} mule_empty;
typedef union { int value; } mule_single_union;
typedef struct { int values[1]; } mule_single_array;
typedef struct { int values[2]; } mule_pair_array;
typedef struct __attribute__((aligned(16))) { int value; } mule_aligned_single;
typedef struct { int value; unsigned char padding[12]; } mule_padded_single;
typedef struct { short value; } mule_narrow_single;

int native_mule_array_in_out_pairs(
    mule_pair *values,
    mule_uintptr count,
    mule_uintptr capacity)
{
    if (count > capacity || (values == 0 && count != 0)) return -7;
    for (mule_uintptr index = 0; index < count; index++)
    {
        values[index].integer += (int)index + 1;
        values[index].real *= -2;
    }
    return 42;
}

__attribute__((noinline)) mule_single_int native_mule_aggregate_int(mule_single_int value)
{
    value.value += 3;
    return value;
}

__attribute__((noinline)) mule_single_double native_mule_aggregate_double(mule_single_double value)
{
    value.value *= -2;
    return value;
}

__attribute__((noinline)) mule_single_pointer native_mule_aggregate_pointer(mule_single_pointer value)
{
    value.value += 3;
    return value;
}

__attribute__((noinline)) mule_nested_single native_mule_aggregate_singleton(mule_nested_single value)
{
    value.inner.value -= 4;
    return value;
}

__attribute__((noinline)) mule_pair native_mule_aggregate_pair(mule_pair value, int delta)
{
    value.integer += delta;
    value.real = -value.real;
    return value;
}

__attribute__((noinline)) mule_pair native_mule_aggregate_mix(mule_pair left, mule_pair right, int delta)
{
    /* Force observable writes to both ordinary by-value parameter objects. */
    volatile mule_pair *first = &left;
    volatile mule_pair *second = &right;
    first->integer += delta;
    second->integer -= delta;
    first->real = -first->real;
    second->real *= 2;
    mule_pair result = { first->integer + second->integer + delta, first->real + second->real };
    return result;
}

__attribute__((noinline)) mule_nested native_mule_aggregate_nested(mule_nested value)
{
    value.pair.integer += 1;
    value.pair.real *= 2;
    value.pointer += 7;
    return value;
}

__attribute__((noinline)) mule_union_scalar native_mule_aggregate_union(mule_union_scalar value)
{
    value.integer ^= 0x80000000U;
    return value;
}

__attribute__((noinline)) mule_union_pair native_mule_aggregate_wide_union(mule_union_pair value)
{
    value.pair.integer += 2;
    value.pair.real *= 3;
    return value;
}

__attribute__((noinline)) mule_packed native_mule_aggregate_packed(mule_packed value)
{
    value.marker += 1;
    value.value -= 9;
    return value;
}

__attribute__((noinline)) mule_empty native_mule_aggregate_empty(mule_empty value)
{
    return value;
}

__attribute__((noinline)) mule_single_union native_mule_aggregate_single_union(mule_single_union value)
{
    value.value += 3;
    return value;
}

__attribute__((noinline)) mule_single_array native_mule_aggregate_single_array(mule_single_array value)
{
    value.values[0] += 3;
    return value;
}

__attribute__((noinline)) mule_pair_array native_mule_aggregate_pair_array(mule_pair_array value)
{
    value.values[0] += value.values[1];
    return value;
}

__attribute__((noinline)) mule_aligned_single native_mule_aggregate_aligned(mule_aligned_single value)
{
    value.value += 3;
    return value;
}

__attribute__((noinline)) mule_padded_single native_mule_aggregate_padded(mule_padded_single value)
{
    value.value += 3;
    return value;
}

__attribute__((noinline)) mule_narrow_single native_mule_aggregate_narrow(mule_narrow_single value)
{
    value.value -= 33;
    return value;
}

/* Scalar observations are independent of managed layout and aggregate lowering. */
unsigned int native_mule_aggregate_layout(unsigned int item)
{
    switch (item)
    {
        case 0: return sizeof(mule_single_int);
        case 1: return _Alignof(mule_single_int);
        case 2: return sizeof(mule_single_double);
        case 3: return _Alignof(mule_single_double);
        case 4: return sizeof(mule_single_pointer);
        case 5: return _Alignof(mule_single_pointer);
        case 6: return sizeof(mule_nested_single);
        case 7: return _Alignof(mule_nested_single);
        case 8: return sizeof(mule_pair);
        case 9: return _Alignof(mule_pair);
        case 10: return __builtin_offsetof(mule_pair, real);
        case 11: return sizeof(mule_nested);
        case 12: return _Alignof(mule_nested);
        case 13: return __builtin_offsetof(mule_nested, pointer);
        case 14: return sizeof(mule_union_scalar);
        case 15: return _Alignof(mule_union_scalar);
        case 16: return sizeof(mule_union_pair);
        case 17: return _Alignof(mule_union_pair);
        case 18: return sizeof(mule_packed);
        case 19: return _Alignof(mule_packed);
        case 20: return __builtin_offsetof(mule_packed, value);
        case 21: return sizeof(mule_empty);
        case 22: return _Alignof(mule_empty);
        case 23: return sizeof(mule_single_union);
        case 24: return _Alignof(mule_single_union);
        case 25: return sizeof(mule_single_array);
        case 26: return _Alignof(mule_single_array);
        case 27: return sizeof(mule_pair_array);
        case 28: return _Alignof(mule_pair_array);
        case 29: return sizeof(mule_aligned_single);
        case 30: return _Alignof(mule_aligned_single);
        case 31: return sizeof(mule_padded_single);
        case 32: return _Alignof(mule_padded_single);
        case 33: return sizeof(mule_narrow_single);
        case 34: return _Alignof(mule_narrow_single);
        default: return 0xffffffffU;
    }
}

int native_mule_aggregate_selftest(void)
{
    volatile int input = 39;
    mule_single_int integer = { input };
    mule_single_double real = { 1.25 };
    mule_single_pointer pointer = { 100 };
    mule_nested_single singleton = { { input } };
    mule_pair pair = { input, 1.25 };
    mule_nested nested = { pair, 100 };
    mule_union_scalar scalar_union = { .integer = 0x3f800000U };
    mule_union_pair wide_union = { .pair = pair };
    mule_packed packed = { 7, input };
    mule_empty empty;
    mule_single_union single_union = { .value = input };
    mule_single_array single_array = { { input } };
    mule_pair_array pair_array = { { input, 3 } };
    mule_aligned_single aligned = { input };
    mule_padded_single padded = { .value = input };
    mule_narrow_single narrow = { -1200 };

    if (native_mule_aggregate_int(integer).value != 42 || integer.value != 39) return 1;
    if (native_mule_aggregate_double(real).value != -2.5 || real.value != 1.25) return 2;
    if (native_mule_aggregate_pointer(pointer).value != 103 || pointer.value != 100) return 3;
    if (native_mule_aggregate_singleton(singleton).inner.value != 35 || singleton.inner.value != 39) return 4;
    mule_pair pair_result = native_mule_aggregate_pair(pair, 3);
    if (pair_result.integer != 42 || pair_result.real != -1.25 || pair.integer != 39 || pair.real != 1.25) return 5;
    mule_nested nested_result = native_mule_aggregate_nested(nested);
    if (nested_result.pair.integer != 40 || nested_result.pair.real != 2.5 || nested_result.pointer != 107 || nested.pointer != 100) return 6;
    if (native_mule_aggregate_union(scalar_union).real != -1.0f || scalar_union.real != 1.0f) return 7;
    mule_union_pair union_result = native_mule_aggregate_wide_union(wide_union);
    if (union_result.pair.integer != 41 || union_result.pair.real != 3.75 || wide_union.pair.integer != 39) return 8;
    mule_packed packed_result = native_mule_aggregate_packed(packed);
    if (packed_result.marker != 8 || packed_result.value != 30 || packed.marker != 7 || packed.value != 39) return 9;
    (void)native_mule_aggregate_empty(empty);
    if (native_mule_aggregate_single_union(single_union).value != 42 || single_union.value != 39) return 10;
    if (native_mule_aggregate_single_array(single_array).values[0] != 42 || single_array.values[0] != 39) return 11;
    if (native_mule_aggregate_pair_array(pair_array).values[0] != 42 || pair_array.values[0] != 39 || pair_array.values[1] != 3) return 12;
    if (native_mule_aggregate_aligned(aligned).value != 42 || aligned.value != 39) return 13;
    if (native_mule_aggregate_padded(padded).value != 42 || padded.value != 39) return 14;
    if (native_mule_aggregate_narrow(narrow).value != -1233 || narrow.value != -1200) return 15;
    return 42;
}
