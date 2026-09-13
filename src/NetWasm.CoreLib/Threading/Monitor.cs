namespace System.Threading
{
    public static class Monitor
    {
        public static void Enter(object value, ref bool lockTaken)
        {
            if (lockTaken)
            {
                throw new System.ArgumentException();
            }
            if (value == null)
            {
                throw new System.ArgumentNullException();
            }
            lockTaken = true;
        }

        public static void Exit(object value)
        {
            if (value == null)
            {
                throw new System.ArgumentNullException();
            }
        }

        public static bool Wait(object value) =>
            throw new System.PlatformNotSupportedException();

        public static void Pulse(object value) =>
            throw new System.PlatformNotSupportedException();

        public static void PulseAll(object value) =>
            throw new System.PlatformNotSupportedException();
    }
}
