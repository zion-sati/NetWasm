// C++ exceptions inside native code: thrown, caught by type and by catch-all, and rethrown. Built freestanding.

struct Boom
{
    int value;
};

static int __attribute__((noinline)) thrower(int value)
{
    if (value > 0)
        throw Boom{value};
    return value;
}

extern "C" int native_runtime_catch(int value)
{
    try
    {
        return thrower(value);
    }
    catch (const Boom &boom)
    {
        return boom.value * 10;
    }
    catch (...)
    {
        return -2;
    }
}

extern "C" int native_runtime_rethrow(int value)
{
    try
    {
        try
        {
            thrower(value);
        }
        catch (Boom &boom)
        {
            boom.value++;
            throw;
        }
    }
    catch (Boom &boom)
    {
        return boom.value;
    }
    return -1;
}

extern "C" int native_runtime_catch_int(int value)
{
    try
    {
        if (value != 0)
            throw value;
        return 0;
    }
    catch (int caught)
    {
        return caught + 1;
    }
}
