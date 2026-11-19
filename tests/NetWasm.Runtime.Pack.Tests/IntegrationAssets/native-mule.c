// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

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
static unsigned int native_mule_handle_replacement_releases;

void native_mule_handle_reset(void)
{
    native_mule_handle_borrows = 0;
    native_mule_handle_releases = 0;
    native_mule_handle_replacement_releases = 0;
}

int native_mule_handle_borrow(mule_uintptr handle)
{
    native_mule_handle_borrows++;
    return handle == 0x1234 ? 42 : -7;
}

int native_mule_handle_release(mule_uintptr handle)
{
    if (handle == 0x5678)
    {
        native_mule_handle_replacement_releases++;
        return 42;
    }
    if (handle != 0x1234) return -7;
    native_mule_handle_releases++;
    return 42;
}

int native_mule_handle_borrow_count(void) { return (int)native_mule_handle_borrows; }
int native_mule_handle_release_count(void) { return (int)native_mule_handle_releases; }
int native_mule_handle_replacement_release_count(void) { return (int)native_mule_handle_replacement_releases; }

/* Handles native code makes, writes, replaces and keeps, and two borrowed at once; a replacement is 0x5678. */
mule_uintptr native_mule_handle_create(void) { return 0x1234; }

int native_mule_handle_open(mule_uintptr *handle)
{
    *handle = 0x1234;
    return 42;
}

int native_mule_handle_open_failed(mule_uintptr *handle)
{
    (void)handle;
    return -1;
}

int native_mule_handle_replace(mule_uintptr *handle)
{
    if (*handle != 0x1234) return -7;
    *handle = 0x5678;
    return 42;
}

int native_mule_handle_keep(mule_uintptr *handle) { return *handle == 0x1234 ? 42 : -7; }

int native_mule_handle_pair(mule_uintptr first, mule_uintptr second)
{
    native_mule_handle_borrows += 2;
    return first == 0x1234 && second == 0x1234 ? 42 : -7;
}

/* Arrays a P/Invoke passes pinned. */
struct mule_point { int x; short y; };

int native_mule_array_is_null(const void *values) { return values == 0; }

void native_mule_array_fill(int *values, int count)
{
    for (int index = 0; index < count; index++)
        values[index] = index * 3 + 1;
}

/* Values a P/Invoke passes by reference: a pointer it advances and a struct it scales. */
int native_mule_pointer_advance(const unsigned char **cursor)
{
    int value = **cursor;
    (*cursor)++;
    return value;
}

void native_mule_point_scale(struct mule_point *point, int factor)
{
    point->x *= factor;
    point->y = (short)(point->y * factor);
}

/* bool and char as desktop .NET's runtime marshalling passes them on Unix: a 4-byte BOOL or a one-byte bool, an
   ANSI char as one byte, a UTF-16 char as two. */
int native_mule_int_echo(int value)
{
    return value;
}

unsigned char native_mule_byte_echo(int value)
{
    return (unsigned char)value;
}

int native_mule_byte_in(unsigned char value)
{
    return value;
}

int native_mule_short_in(unsigned short value)
{
    return value;
}

unsigned short native_mule_short_echo(int value)
{
    return (unsigned short)value;
}

void native_mule_int_bump(int *value)
{
    *value += 5;
}

int native_mule_int_read(int *value)
{
    int seen = *value;
    *value = 0;
    return seen;
}

void native_mule_byte_bump(unsigned char *value)
{
    *value = (unsigned char)(*value + 1);
}

void native_mule_short_bump(unsigned short *value)
{
    *value = (unsigned short)(*value + 1);
}

/* Strings as desktop .NET's runtime marshalling passes them on Unix: NUL-terminated UTF-8 or UTF-16. */
int native_mule_text_sum(const unsigned char *text)
{
    if (!text)
        return -1;
    int sum = 0;
    for (; *text; text++)
        sum = sum * 31 + *text;
    return sum;
}

int native_mule_wide_sum(const unsigned short *text)
{
    if (!text)
        return -1;
    int sum = 0;
    for (; *text; text++)
        sum = sum * 31 + *text;
    return sum;
}

void native_mule_text_poke(unsigned char *text)
{
    if (text && text[0])
        text[0] = 'Z';
}

void native_mule_wide_poke(unsigned short *text)
{
    if (text && text[0])
        text[0] = 'Z';
}

void *malloc(__SIZE_TYPE__ size);
void free(void *block);

/* A copy in the C library's memory, which desktop .NET frees with CoTaskMemFree, the C library's free on Unix. */
static void *native_mule_copy(const void *source, __SIZE_TYPE__ bytes)
{
    unsigned char *copy = malloc(bytes);
    for (__SIZE_TYPE__ index = 0; index < bytes; index++)
        copy[index] = ((const unsigned char *)source)[index];
    return copy;
}

/* NUL-terminated string results: null, empty, ASCII, and UTF-8 or UTF-16 past ASCII. */
char *native_mule_text_name(int variant)
{
    static const char empty[] = "";
    static const char mule[] = "mule";
    static const char accented[] = "h\xc3\xa9llo\xe2\x82\xac";
    if (variant < 0)
        return 0;
    return native_mule_copy(variant == 0 ? empty : variant == 1 ? mule : accented,
        variant == 0 ? sizeof(empty) : variant == 1 ? sizeof(mule) : sizeof(accented));
}

unsigned short *native_mule_wide_name(int variant)
{
    static const unsigned short empty[] = { 0 };
    static const unsigned short mule[] = { 'm', 'u', 'l', 'e', 0 };
    static const unsigned short accented[] = { 0x00e9, 0x20ac, 0 };
    if (variant < 0)
        return 0;
    return native_mule_copy(variant == 0 ? empty : variant == 1 ? mule : accented,
        variant == 0 ? sizeof(empty) : variant == 1 ? sizeof(mule) : sizeof(accented));
}

/* A block of the C library's, numbered 1 to size, which managed code frees with Marshal.FreeCoTaskMem. */
unsigned char *native_mule_allocate(int size)
{
    unsigned char *block = malloc((__SIZE_TYPE__)size);
    for (int index = 0; index < size; index++)
        block[index] = (unsigned char)(index + 1);
    return block;
}

/* The sum of a block Marshal allocated, which native code frees with the C library's free. */
int native_mule_release(unsigned char *block, int size)
{
    int sum = 0;
    for (int index = 0; index < size; index++)
        sum += block[index];
    free(block);
    return sum;
}

int native_mule_array_points(const struct mule_point *points, int count)
{
    int sum = 0;
    for (int index = 0; index < count; index++)
        sum += points[index].x * 10 + points[index].y;
    return sum;
}

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

/* Issue 226: native functions managed code calls through unmanaged function pointers. */
typedef struct mule_calli_pair { int first; int second; } mule_calli_pair;

static int mule_calli_twice(int value) { return value * 2; }

static mule_calli_pair mule_calli_swap(mule_calli_pair value) {
    mule_calli_pair swapped = { value.second, value.first };
    return swapped;
}

static double mule_calli_mix(int integer, double real, mule_int64 wide) {
    return (double)integer + real + (double)wide;
}

void *native_mule_calli_twice_address(void) { return (void *)&mule_calli_twice; }
void *native_mule_calli_swap_address(void) { return (void *)&mule_calli_swap; }
void *native_mule_calli_mix_address(void) { return (void *)&mule_calli_mix; }

/* Issue 226's siblings: float, narrow integers the C ABI extends, a pointer-width argument and a
 * struct of one scalar, which the C ABI passes as that scalar rather than through a pointer. */
typedef struct mule_calli_wrapped { double value; } mule_calli_wrapped;

static float mule_calli_scale(float value, float factor) { return value * factor; }

static signed char mule_calli_narrow(unsigned char unsigned_value, signed char signed_value) {
    return (signed char)(unsigned_value + signed_value);
}

static mule_int64 mule_calli_sum(const int *values, int count) {
    mule_int64 sum = 0;
    for (int index = 0; index < count; index++) sum += values[index];
    return sum * 4294967296LL;
}

static mule_calli_wrapped mule_calli_wrap(mule_calli_wrapped wrapped) {
    mule_calli_wrapped doubled = { wrapped.value * 2.0 };
    return doubled;
}

void *native_mule_calli_scale_address(void) { return (void *)&mule_calli_scale; }
void *native_mule_calli_narrow_address(void) { return (void *)&mule_calli_narrow; }
void *native_mule_calli_sum_address(void) { return (void *)&mule_calli_sum; }
void *native_mule_calli_wrap_address(void) { return (void *)&mule_calli_wrap; }

/* Narrow integers in an import's own signature, extended as the C ABI extends them. */
short native_mule_narrow_import(signed char signed_value, unsigned short unsigned_value) {
    return (short)(signed_value * 2 - (int)unsigned_value);
}

/* Structs of callbacks passed by value, as SkiaSharp passes its stream callbacks: two travel by address,
   one as its scalar. C calls each. */
typedef struct mule_procs { int (*add)(int, int); void (*store)(int); } mule_procs;
typedef struct mule_proc { int (*apply)(int); } mule_proc;

int native_mule_call_procs(mule_procs procs, int value) {
    procs.store(value + 100);
    return procs.add(value, 1);
}

int native_mule_call_proc(mule_proc proc, int value) { return proc.apply(value); }

/* Under disabled runtime marshalling, .NET passes bool as C's one-byte bool and char as char16_t: in arguments
   and results, struct fields, a callback's signature and a call through a function pointer. */
typedef struct mule_flagged { _Bool flag; unsigned short letter; int value; } mule_flagged;

_Bool native_mule_flag_not(_Bool flag) { return !flag; }
unsigned short native_mule_char_next(unsigned short letter) { return (unsigned short)(letter + 1); }
int native_mule_flag_width(_Bool flag) { return flag ? 1 : 0; }

mule_flagged native_mule_flagged_flip(mule_flagged value) {
    mule_flagged flipped = { !value.flag, (unsigned short)(value.letter + 1), value.value ^ 0x55 };
    return flipped;
}

int native_mule_flag_callback(_Bool (*predicate)(int, unsigned short), int value) {
    return predicate(value, (unsigned short)'A') ? 1 : 2;
}

static _Bool mule_flag_and(_Bool flag, unsigned short letter) { return flag && letter == (unsigned short)'Z'; }
void *native_mule_flag_and_address(void) { return (void *)&mule_flag_and; }


/* C variadic functions, which P/Invokes declare through fixed-signature overloads, as SQLitePCLRaw declares
   sqlite3_config and sqlite3_db_config. */
int native_mule_config(int op, ...)
{
    __builtin_va_list arguments;
    __builtin_va_start(arguments, op);
    int result = -1;
    if (op == 0)
    {
        result = 100;
    }
    else if (op == 1)
    {
        result = __builtin_va_arg(arguments, int) * 2;
    }
    else if (op == 2)
    {
        mule_intptr pointer = __builtin_va_arg(arguments, mule_intptr);
        int* out = __builtin_va_arg(arguments, int*);
        *out = (int)pointer + 1;
        result = 7;
    }
    else if (op == 3)
    {
        mule_int64 wide = __builtin_va_arg(arguments, mule_int64);
        int last = __builtin_va_arg(arguments, int);
        result = (int)(wide >> 32) + last;
    }
    else if (op == 4)
    {
        int first = __builtin_va_arg(arguments, int);
        double promoted = __builtin_va_arg(arguments, double);
        double exact = __builtin_va_arg(arguments, double);
        result = first + (int)(promoted * 4) + (int)(exact * 2);
    }
    __builtin_va_end(arguments);
    return result;
}

int native_mule_db_config(mule_intptr db, int op, ...)
{
    __builtin_va_list arguments;
    __builtin_va_start(arguments, op);
    int value = op == 5 ? (int)__builtin_va_arg(arguments, mule_intptr) : __builtin_va_arg(arguments, int);
    int* out = __builtin_va_arg(arguments, int*);
    *out = (int)db * 10 + op + value;
    __builtin_va_end(arguments);
    return 0;
}

/* Under runtime marshalling, a struct holding a bool or char passes as desktop .NET lays it out natively on Unix: bool
   as a 4-byte BOOL (or a byte with [MarshalAs(U1)]), char as one ANSI byte or, under CharSet.Unicode, a UTF-16 code
   unit; by value, by reference and as a result, a single-field struct as its scalar. */
typedef struct mule_layout_flagged { int a; int b; int c; } mule_layout_flagged;
typedef struct mule_layout_flag { int b; } mule_layout_flag;
typedef struct mule_layout_ansi_pair { unsigned char x; unsigned char c; } mule_layout_ansi_pair;
typedef struct mule_layout_wide_pair { unsigned char x; unsigned short c; } mule_layout_wide_pair;
typedef struct mule_layout_byte_flag { int a; unsigned char b; } mule_layout_byte_flag;
typedef struct mule_layout_outer { mule_layout_flagged inner; int flag; } mule_layout_outer;
typedef struct mule_layout_measure { double d; int b; } mule_layout_measure;
typedef struct mule_layout_pointer_flag { void* p; int b; } mule_layout_pointer_flag;

int native_mule_layout_flagged_sum(mule_layout_flagged s) { return s.a * 100 + s.b * 10 + s.c; }
int native_mule_layout_flag_value(mule_layout_flag s) { return s.b; }
int native_mule_layout_ansi_pair_sum(mule_layout_ansi_pair s) { return s.x * 1000 + s.c; }
int native_mule_layout_wide_pair_sum(mule_layout_wide_pair s) { return s.x * 100000 + s.c; }
int native_mule_layout_byte_flag_sum(mule_layout_byte_flag s) { return s.a * 10 + s.b; }
int native_mule_layout_outer_sum(mule_layout_outer o) {
    return o.inner.a * 1000 + o.inner.b * 100 + o.inner.c * 10 + o.flag;
}
mule_layout_flagged native_mule_layout_flagged_make(int a, int b) { mule_layout_flagged s = { a, b, a + b }; return s; }
mule_layout_flag native_mule_layout_flag_make(int b) { mule_layout_flag s = { b }; return s; }
mule_layout_measure native_mule_layout_measure_make(double d, int b) { mule_layout_measure s = { d, b }; return s; }
void native_mule_layout_flagged_bump(mule_layout_flagged* s) { s->a += 1; s->b = !s->b; s->c *= 2; }
void native_mule_layout_ansi_pair_set(mule_layout_ansi_pair* s) { s->x = (unsigned char)(s->x + s->c); s->c = 'Z'; }
void native_mule_layout_ansi_pair_fill(mule_layout_ansi_pair* s) { s->x = 7; s->c = 'Y'; }
int native_mule_layout_pointer_flag_sum(mule_layout_pointer_flag s) { return (int)(__INTPTR_TYPE__)s.p + s.b * 1000; }
mule_layout_pointer_flag native_mule_layout_pointer_flag_make(void* p, int b) {
    mule_layout_pointer_flag s = { p, b };
    return s;
}
typedef struct mule_layout_wide_buffer { int a; unsigned short text[3]; } mule_layout_wide_buffer;
int native_mule_layout_wide_buffer_sum(mule_layout_wide_buffer s) {
    return s.a * 1000 + s.text[0] * 100 + s.text[1] * 10 + s.text[2];
}
void native_mule_layout_wide_buffer_bump(mule_layout_wide_buffer* s) {
    s->a += 1;
    for (int i = 0; i < 3; i++)
        s->text[i] = (unsigned short)(s->text[i] * 2);
}

// The functions the raw-marshalling mule calls through unmanaged function pointers, by number.
void *native_mule_raw_function(int which)
{
    switch (which)
    {
    case 0: return (void *)native_mule_int_echo;
    case 1: return (void *)native_mule_byte_in;
    case 2: return (void *)native_mule_byte_echo;
    case 3: return (void *)native_mule_text_sum;
    case 4: return (void *)native_mule_layout_flagged_sum;
    case 5: return (void *)native_mule_layout_flagged_make;
    default: return 0;
    }
}
