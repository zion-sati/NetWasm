#nullable enable

using System;
using System.Runtime.InteropServices.JavaScript;

public static class ConsumerMath
{
    [JSImport("add", "consumer.math")]
    public static extern int Add(int left, int right);

    [JSImport("add_i64", "consumer.math")]
    public static extern long AddInt64(long left, long right);

    [JSImport("add_u64", "consumer.math")]
    public static extern ulong AddUInt64(ulong left, ulong right);

    [JSImport("scale_f32", "consumer.math")]
    public static extern float ScaleSingle(float value);

    [JSImport("scale_f64", "consumer.math")]
    public static extern double ScaleDouble(double value);

    [JSImport("primitive_flags", "consumer.math")]
    public static extern int PrimitiveFlags(
        bool boolean,
        byte unsignedByte,
        sbyte signedByte,
        short signedShort,
        ushort unsignedShort,
        uint unsignedInt,
        char character);

    [JSImport("native_identity", "consumer.math")]
    public static extern nint NativeIdentity(nint value);

    [JSImport("native_unsigned_identity", "consumer.math")]
    public static extern nuint NativeUnsignedIdentity(nuint value);

    [JSImport("bool_identity", "consumer.math")]
    public static extern bool BoolIdentity(bool value);

    [JSImport("sbyte_identity", "consumer.math")]
    public static extern sbyte SByteIdentity(sbyte value);

    [JSImport("byte_identity", "consumer.math")]
    public static extern byte ByteIdentity(byte value);

    [JSImport("short_identity", "consumer.math")]
    public static extern short Int16Identity(short value);

    [JSImport("ushort_identity", "consumer.math")]
    public static extern ushort UInt16Identity(ushort value);

    [JSImport("char_identity", "consumer.math")]
    public static extern char CharIdentity(char value);

    [JSImport("uint_identity", "consumer.math")]
    public static extern uint UInt32Identity(uint value);

    [JSImport("ulong_identity", "consumer.math")]
    public static extern ulong UInt64Identity(ulong value);

    [JSImport("report", "consumer.math")]
    public static extern void Report(int value);

    [JSImport("receive", "consumer.math")]
    public static extern void Receive(string? value);

    [JSImport("echo", "consumer.math")]
    public static extern string? Echo(string? value);

    [JSImport("receive_bytes", "consumer.math")]
    public static extern void ReceiveBytes(byte[]? value);

    [JSImport("echo_bytes", "consumer.math")]
    public static extern byte[]? EchoBytes(byte[]? value);

    [JSImport("create_object", "consumer.math")]
    public static extern JSObject? CreateObject();

    [JSImport("object_value", "consumer.math")]
    public static extern int ObjectValue(JSObject? value);

    [JSImport("subscribe", "consumer.math")]
    public static extern JSSubscription Subscribe(Transform callback);

    [JSImport("invoke_subscription", "consumer.math")]
    public static extern int InvokeSubscription(JSSubscription subscription, int value);

    [JSImport("subscribe_string", "consumer.math")]
    public static extern JSSubscription SubscribeString(StringTransform callback);

    [JSImport("invoke_string_subscription", "consumer.math")]
    public static extern int InvokeStringSubscription(JSSubscription subscription, string? value);

    [JSImport("subscribe_bytes", "consumer.math")]
    public static extern JSSubscription SubscribeBytes(ByteTransform callback);

    [JSImport("invoke_bytes_subscription", "consumer.math")]
    public static extern int InvokeBytesSubscription(JSSubscription subscription, byte[]? value);

    [JSImport("subscribe_browser_scalar_event", "consumer.math")]
    public static extern JSSubscription SubscribeBrowserScalarEvent(Transform callback);

    [JSImport("subscribe_browser_string_event", "consumer.math")]
    public static extern JSSubscription SubscribeBrowserStringEvent(StringTransform callback);

    [JSImport("subscribe_browser_bytes_event", "consumer.math")]
    public static extern JSSubscription SubscribeBrowserBytesEvent(ByteTransform callback);
}

public static class BrowserServices
{
    [JSImport("console_log_i32", "consumer.browser")]
    public static extern void ConsoleLog(int value);

    [JSImport("read_monotonic_probe", "consumer.browser")]
    public static extern long GetMonotonicMilliseconds();

    [JSImport("read_wall_clock_probe", "consumer.browser")]
    public static extern long GetWallClockUnixMilliseconds();

    [JSImport("schedule_microtask_probe", "consumer.browser")]
    public static extern JSSubscription QueueMicrotask(Action callback);

    [JSImport("schedule_timeout_probe", "consumer.browser")]
    public static extern JSSubscription SetTimeout(Action callback, int delayMilliseconds);
}

public delegate int Transform(int value);
public delegate int StringTransform(string? value);
public delegate int ByteTransform(byte[]? value);

public static class EntryPoint
{
    private static JSSubscription? _microtask;
    private static JSSubscription? _timer;
    private static JSSubscription? _browserScalarEvent;
    private static JSSubscription? _browserStringEvent;
    private static JSSubscription? _browserBytesEvent;
    private static int _scheduledCallbackCount;
    private static int _browserEventState;

    public static int Run(int input)
    {
        ConsumerMath.Receive("a\0Ω\ud800");
        ConsumerMath.Receive(null);
        var bytes = new byte[3];
        bytes[0] = 1;
        bytes[1] = 2;
        bytes[2] = 3;
        ConsumerMath.ReceiveBytes(bytes);
        ConsumerMath.ReceiveBytes(null);
        return ConsumerMath.Add(input, 2);
    }

    [JSExport("twice")]
    public static int Twice(int input) => input * 2;

    [JSExport("primitive_scalar_lifecycle")]
    public static int PrimitiveScalarLifecycle(int input)
    {
        var signed64 = ConsumerMath.AddInt64(4_294_967_296L, 7L);
        var unsigned64 = ConsumerMath.AddUInt64(9UL, 12UL);
        var single = ConsumerMath.ScaleSingle(1.25f);
        var @double = ConsumerMath.ScaleDouble(1.5d);
        var narrow = ConsumerMath.PrimitiveFlags(true, 1, -2, -3, 4, 5U, 'A');
        return signed64 == 4_294_967_303L && unsigned64 == 21UL &&
               single == 2.5f && @double == 3d && narrow == 71
            ? input
            : 0;
    }

    [JSExport("primitive_boundary_lifecycle")]
    public static int PrimitiveBoundaryLifecycle(int input)
    {
        return ConsumerMath.BoolIdentity(true) &&
               ConsumerMath.SByteIdentity(-128) == -128 &&
               ConsumerMath.ByteIdentity(255) == 255 &&
               ConsumerMath.Int16Identity(-32768) == -32768 &&
               ConsumerMath.UInt16Identity(65535) == 65535 &&
               ConsumerMath.CharIdentity('\ud800') == '\ud800' &&
               ConsumerMath.UInt32Identity(uint.MaxValue) == uint.MaxValue &&
               ConsumerMath.UInt64Identity(18_446_744_073_709_551_615UL) == 18_446_744_073_709_551_615UL
            ? input
            : 0;
    }

    [JSExport("primitive_storage_lifecycle")]
    public static int PrimitiveStorageLifecycle(int input)
    {
        var storage = new PrimitiveStorage
        {
            SignedByte = -128,
            Signed64 = -4_294_967_296L,
            Single = 1.25f,
            Double = 1.5d,
        };
        var signed64 = new long[1];
        signed64[0] = storage.Signed64;
        var doubles = new double[1];
        doubles[0] = storage.Double;
        return storage.SignedByte == -128 && signed64[0] == -4_294_967_296L &&
               storage.Single == 1.25f && doubles[0] == 1.5d
            ? input
            : 0;
    }

    [JSExport("native_int_lifecycle")]
    public static int NativeIntLifecycle(int input)
    {
        var value = ConsumerMath.NativeIdentity((nint)input);
        return value == input ? input : 0;
    }

    [JSExport("native_width_lifecycle")]
    public static int NativeWidthLifecycle(int input)
    {
        var value = ConsumerMath.NativeIdentity(unchecked((nint)4_294_967_296L));
        return value > (nint)System.Int32.MaxValue ? input : 0;
    }

    [JSExport("native_unsigned_width_lifecycle")]
    public static int NativeUnsignedWidthLifecycle(int input)
    {
        var value = ConsumerMath.NativeUnsignedIdentity(unchecked((nuint)4_294_967_296UL));
        return value > (nuint)System.UInt32.MaxValue ? input : 0;
    }

    [JSExport("failure_status")]
    public static int FailureStatus(int input)
    {
        try
        {
            return ConsumerMath.Add(-1, input);
        }
        catch (System.JSException)
        {
            return input + 1;
        }
    }

    [JSExport("report_then_twice")]
    public static int ReportThenTwice(int input)
    {
        ConsumerMath.Report(input);
        return input * 2;
    }

    [JSExport("echo_code_units")]
    public static int EchoCodeUnits(int input)
    {
        var value = ConsumerMath.Echo("a\0Ω\ud800");
        var nullValue = ConsumerMath.Echo(null);
        if (value is null || nullValue is not null || value.Length != 5)
        {
            return 0;
        }
        return value[0] == 'a' && value[1] == '\0' && value[2] == 'Ω' &&
               value[3] == '\ud800' && value[4] == '\udc00'
            ? input
            : 0;
    }

    [JSExport("echo_bytes_checksum")]
    public static int EchoBytesChecksum(int input)
    {
        var bytes = new byte[3];
        bytes[0] = 1;
        bytes[1] = 2;
        bytes[2] = 3;
        var result = ConsumerMath.EchoBytes(bytes);
        var nullResult = ConsumerMath.EchoBytes(null);
        if (result is null || nullResult is not null || result.Length != 4)
        {
            return 0;
        }
        return result[0] == 1 && result[1] == 2 && result[2] == 3 && result[3] == 4
            ? input
            : 0;
    }

    [JSExport("object_lifecycle")]
    public static int ObjectLifecycle(int input)
    {
        var value = ConsumerMath.CreateObject();
        if (value is null || ConsumerMath.ObjectValue(value) != input)
        {
            return 0;
        }
        value.Dispose();
        return input;
    }

    [JSExport("wide_comparison_lifecycle")]
    public static int WideComparisonLifecycle(int input)
    {
        long left64 = input;
        long right64 = input + 1;
        float left32 = input;
        float right32 = input + 1;
        double leftDouble = input;
        double rightDouble = input + 1;
        nint leftNative = input;
        nint rightNative = input + 1;
        if (!(left64 < right64 && left64 <= right64 && right64 > left64 &&
              right64 >= left64 && left64 != right64 &&
              left32 < right32 && left32 <= right32 && right32 > left32 &&
              right32 >= left32 && left32 != right32 &&
              leftDouble < rightDouble && leftDouble <= rightDouble &&
              rightDouble > leftDouble && rightDouble >= leftDouble &&
              leftDouble != rightDouble && leftNative < rightNative &&
              leftNative <= rightNative && rightNative > leftNative &&
              rightNative >= leftNative && leftNative != rightNative))
        {
            return 0;
        }
        float nan32 = 0f / 0f;
        double nan64 = 0d / 0d;
        if (Equal(nan32, nan32) || nan32 < left32 || nan32 > left32 ||
            Equal(nan64, nan64) || nan64 < leftDouble || nan64 > leftDouble)
        {
            return 0;
        }
        return input;
    }

    [JSExport("retain_object_until_unload")]
    public static int RetainObjectUntilUnload(int input)
    {
        return ConsumerMath.CreateObject() is null ? 0 : input;
    }

    [JSExport("retain_subscription_until_unload")]
    public static int RetainSubscriptionUntilUnload(int input)
    {
        return ConsumerMath.Subscribe(Double) is null ? 0 : input;
    }

    [JSExport("subscription_lifecycle")]
    public static int SubscriptionLifecycle(int input)
    {
        var target = new CallbackTarget(input);
        var subscription = ConsumerMath.Subscribe(target.Invoke);
        if (ConsumerMath.InvokeSubscription(subscription, input) != input * 2)
        {
            return 0;
        }
        subscription.Dispose();
        try
        {
            ConsumerMath.InvokeSubscription(subscription, input);
            return 0;
        }
        catch (System.JSException)
        {
            return input;
        }
    }

    [JSExport("reentrant_callback_lifecycle")]
    public static int ReentrantCallbackLifecycle(int input)
    {
        var subscription = ConsumerMath.Subscribe(ReentrantCallback);
        var result = ConsumerMath.InvokeSubscription(subscription, input);
        subscription.Dispose();
        return result == 42 ? input : 0;
    }

    [JSExport("failing_callback_lifecycle")]
    public static int FailingCallbackLifecycle(int input)
    {
        var subscription = ConsumerMath.Subscribe(ThrowFromCallback);
        try
        {
            ConsumerMath.InvokeSubscription(subscription, input);
            return 0;
        }
        catch (System.JSException)
        {
            subscription.Dispose();
            return input;
        }
    }

    [JSExport("string_subscription_lifecycle")]
    public static int StringSubscriptionLifecycle(int input)
    {
        var subscription = ConsumerMath.SubscribeString(MeasureString);
        var result = ConsumerMath.InvokeStringSubscription(subscription, "a\0Ω\ud800");
        subscription.Dispose();
        return result == 4 ? input : 0;
    }

    [JSExport("bytes_subscription_lifecycle")]
    public static int BytesSubscriptionLifecycle(int input)
    {
        var value = new byte[3];
        value[0] = 1;
        value[1] = 2;
        value[2] = 3;
        var subscription = ConsumerMath.SubscribeBytes(MeasureBytes);
        var result = ConsumerMath.InvokeBytesSubscription(subscription, value);
        subscription.Dispose();
        return result == 6 ? input : 0;
    }

    [JSExport("begin_browser_event_subscription")]
    public static int BeginBrowserEventSubscription(int input)
    {
        _browserScalarEvent = ConsumerMath.SubscribeBrowserScalarEvent(HandleBrowserScalarEvent);
        _browserStringEvent = ConsumerMath.SubscribeBrowserStringEvent(HandleBrowserStringEvent);
        _browserBytesEvent = ConsumerMath.SubscribeBrowserBytesEvent(HandleBrowserBytesEvent);
        return input;
    }

    [JSExport("browser_event_state")]
    public static int BrowserEventState(int input) => _browserEventState;

    [JSExport("dispose_browser_event_subscription")]
    public static int DisposeBrowserEventSubscription(int input)
    {
        _browserScalarEvent?.Dispose();
        _browserStringEvent?.Dispose();
        _browserBytesEvent?.Dispose();
        _browserScalarEvent = null;
        _browserStringEvent = null;
        _browserBytesEvent = null;
        return input;
    }

    [JSExport("schedule_browser_services")]
    public static int ScheduleBrowserServices(int input)
    {
        BrowserServices.ConsoleLog(input);
        if (BrowserServices.GetMonotonicMilliseconds() < 0)
        {
            return 0;
        }
        if (BrowserServices.GetWallClockUnixMilliseconds() <= 0)
        {
            return 0;
        }
        _microtask = BrowserServices.QueueMicrotask(IncrementScheduledCallbackCount);
        _timer = BrowserServices.SetTimeout(IncrementScheduledCallbackCount, 0);
        return input;
    }

    [JSExport("scheduled_callback_count")]
    public static int ScheduledCallbackCount(int input) => _scheduledCallbackCount;

    [JSExport("dispose_scheduled_services")]
    public static int DisposeScheduledServices(int input)
    {
        if (_microtask is not null)
        {
            _microtask.Dispose();
            _microtask = null;
        }
        if (_timer is not null)
        {
            _timer.Dispose();
            _timer = null;
        }
        return input;
    }

    private static int MeasureString(string? value) => value is null ? -1 : value.Length;

    private static bool Equal(float left, float right) => left == right;

    private static bool Equal(double left, double right) => left == right;

    private static void IncrementScheduledCallbackCount() => _scheduledCallbackCount++;

    private static int Double(int value) => value * 2;

    private static int ReentrantCallback(int value) => ConsumerMath.Add(40, value - 19);

    private static int ThrowFromCallback(int value) => throw new System.ArgumentException();

    private static int MeasureBytes(byte[]? value)
    {
        if (value is null || value.Length != 3)
        {
            return -1;
        }
        return value[0] + value[1] + value[2];
    }

    private static int HandleBrowserScalarEvent(int value)
    {
        if (value == -1) throw new System.ArgumentException();
        if (value != 7) return -1;
        if (ConsumerMath.Add(40, 2) != 42) return -1;
        _browserEventState = _browserEventState * 10 + 1;
        return 91;
    }

    private static int HandleBrowserStringEvent(string? text)
    {
        if (text is null || text.Length != 4 || text[0] != 'a' || text[1] != '\0' ||
            text[2] != 'Ω' || text[3] != '\ud800') return -1;
        _browserEventState = _browserEventState * 10 + 2;
        return 92;
    }

    private static int HandleBrowserBytesEvent(byte[]? bytes)
    {
        if (bytes is null || bytes.Length != 3 || bytes[0] != 1 || bytes[1] != 2 || bytes[2] != 3) return -1;
        _browserEventState = _browserEventState * 10 + 3;
        return 93;
    }

    private sealed class CallbackTarget
    {
        private readonly int _offset;

        public CallbackTarget(int offset)
        {
            _offset = offset;
        }

        public int Invoke(int value)
        {
            var allocation = new object();
            if (allocation is null)
            {
                return 0;
            }
            return value + _offset;
        }
    }

    private sealed class PrimitiveStorage
    {
        public sbyte SignedByte;
        public long Signed64;
        public float Single;
        public double Double;
    }

    [JSExport("throw_managed")]
    public static int ThrowManaged(int input) => throw new System.ArgumentException();
}
