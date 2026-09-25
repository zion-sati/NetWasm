namespace NetWasm.Fixtures.ManagedHeap;

public sealed class Node
{
    public Node(int value)
    {
        Value = value;
    }

    public Node Child;
    public int Value;
}

public sealed class FinalizableGraph
{
    public FinalizableGraph(Node[] nodes, string text)
    {
        Nodes = nodes;
        Text = text;
    }

    ~FinalizableGraph()
    {
        Runs = Runs + 1;
        Observed = Nodes[1].Value + Text[1];
    }

    public static int Runs;
    public static int Observed;
    public Node[] Nodes;
    public string Text;
}

public sealed class EscapingFinalizer
{
    ~EscapingFinalizer()
    {
        throw new DerivedManagedFailure();
    }
}

public class BaseFinalizable
{
    ~BaseFinalizable()
    {
        Trace = Trace * 10 + 1;
    }

    public static int Trace;
}

public sealed class DerivedFinalizable : BaseFinalizable
{
    ~DerivedFinalizable()
    {
        Trace = Trace * 10 + 2;
    }
}

public sealed class SuppressedFinalizable
{
    ~SuppressedFinalizable()
    {
        Runs = Runs + 1;
    }

    public static int Runs;
}

public sealed class ResurrectedFinalizable
{
    ~ResurrectedFinalizable()
    {
        Runs = Runs + 1;
        Saved = this;
    }

    public static int Runs;
    public static ResurrectedFinalizable Saved;
}

public sealed class ReregisteredFinalizable
{
    ~ReregisteredFinalizable()
    {
        Runs = Runs + 1;
        if (Runs == 1)
        {
            Saved = this;
            System.GC.ReRegisterForFinalize(this);
        }
    }

    public static int Runs;
    public static ReregisteredFinalizable Saved;
}

public class ManagedFailure : System.Exception
{
}

public sealed class DerivedManagedFailure : ManagedFailure
{
}

public sealed class OtherManagedFailure : ManagedFailure
{
}

public static class EntryPoint
{
    public static Node[] SavedNodes;
    public static string SavedText;

    public static int Run(int input)
    {
        if (input == -999)
        {
            _ = new EscapingFinalizer();
            _ = new EscapingFinalizer();
            return 0;
        }
        if (input == -998)
        {
            return 0;
        }
        var nodes = new Node[3];
        nodes[0] = new Node(input);
        nodes[1] = new Node(input + 1);
        nodes[2] = new Node(input + 2);
        nodes[0].Child = nodes[1];
        nodes[1].Child = nodes[2];
        nodes[2].Child = nodes[0];

        string text = new string('\ud800', 3);
        _ = new FinalizableGraph(nodes, text);
        SavedNodes = nodes;
        SavedText = text;
        return nodes.Length + nodes[0].Child.Value + text.Length + text[1];
    }

    public static int ValidateRoots(int input)
    {
        return SavedNodes.Length + SavedNodes[2].Value +
               SavedText.Length + SavedText[2] + input;
    }

    public static Node[] GetNodes(int unused)
    {
        return SavedNodes;
    }

    public static int ReadNodes(Node[] nodes)
    {
        return nodes.Length + nodes[0].Child.Value;
    }

    public static int ClearRoots(int unused)
    {
        SavedNodes = null;
        SavedText = null;
        return unused;
    }

    public static int GetFinalizerRuns(int unused)
    {
        return FinalizableGraph.Runs + unused;
    }

    public static int GetFinalizerObserved(int unused)
    {
        return FinalizableGraph.Observed + unused;
    }

    public static int RunTypedCatch(int input)
    {
        try
        {
            ThrowAcrossCall(input);
            return -1;
        }
        catch (ManagedFailure)
        {
            return 81;
        }
    }

    private static void ThrowAcrossCall(int shouldThrow)
    {
        if (shouldThrow != 0)
        {
            throw new DerivedManagedFailure();
        }
    }

    public static int RunRootedUnwind(int unused)
    {
        try
        {
            return ThrowWithLiveRoot();
        }
        catch (ManagedFailure)
        {
            return unused + 401;
        }
    }

    private static int ThrowWithLiveRoot()
    {
        var root = new Node(7);
        ThrowAcrossCall(1);
        return root.Value;
    }

    public static int RunSequentialHandlers(int mode)
    {
        int result = 0;
        try
        {
            if (mode == 1)
            {
                throw new DerivedManagedFailure();
            }
            result = result + 1;
        }
        catch (ManagedFailure)
        {
            result = result + 10;
        }

        try
        {
            if (mode == 2)
            {
                throw new OtherManagedFailure();
            }
            result = result + 2;
        }
        catch (OtherManagedFailure)
        {
            result = result + 20;
        }
        return result;
    }

    public static int RunMultipleLeaves(int mode)
    {
        Trace = 0;
        try
        {
            if (mode == 0)
            {
                goto First;
            }
            if (mode == 1)
            {
                goto Second;
            }
            Trace = 3;
        }
        finally
        {
            Trace = Trace + 10;
        }
        return Trace;

    First:
        return Trace + 100;
    Second:
        return Trace + 200;
    }

    public static int RunAllocationStress(int count)
    {
        Node root = null;
        for (int index = 0; index < count; index++)
        {
            var next = new Node(index);
            next.Child = root;
            root = next;
        }
        return root == null ? -1 : root.Value;
    }

    public static int ThrowUnhandled(int unused)
    {
        throw new DerivedManagedFailure();
    }

    public static int CreateDerivedFinalizable(int unused)
    {
        _ = new DerivedFinalizable();
        return unused;
    }

    public static int GetDerivedFinalizerTrace(int unused)
    {
        return BaseFinalizable.Trace + unused;
    }

    public static int CreateSuppressedFinalizable(int unused)
    {
        var value = new SuppressedFinalizable();
        System.GC.SuppressFinalize(value);
        return unused;
    }

    public static int GetSuppressedFinalizerRuns(int unused)
    {
        return SuppressedFinalizable.Runs + unused;
    }

    public static int CreateResurrectedFinalizable(int unused)
    {
        _ = new ResurrectedFinalizable();
        return unused;
    }

    public static int ReleaseResurrectedFinalizable(int unused)
    {
        ResurrectedFinalizable.Saved = null;
        return unused;
    }

    public static int GetResurrectedFinalizerRuns(int unused)
    {
        return ResurrectedFinalizable.Runs + unused;
    }

    public static int HasResurrectedFinalizable(int unused)
    {
        return ResurrectedFinalizable.Saved == null ? unused : unused + 1;
    }

    public static int CreateReregisteredFinalizable(int unused)
    {
        _ = new ReregisteredFinalizable();
        return unused;
    }

    public static int ReleaseReregisteredFinalizable(int unused)
    {
        ReregisteredFinalizable.Saved = null;
        return unused;
    }

    public static int GetReregisteredFinalizerRuns(int unused)
    {
        return ReregisteredFinalizable.Runs + unused;
    }

    public static int HasReregisteredFinalizable(int unused)
    {
        return ReregisteredFinalizable.Saved == null ? unused : unused + 1;
    }

    public static int CatchNullField(int unused)
    {
        try
        {
            Node value = null;
            return value.Value;
        }
        catch (System.NullReferenceException)
        {
            return unused + 101;
        }
    }

    public static int CatchArrayBounds(int index)
    {
        try
        {
            var values = new Node[1];
            return values[index] == null ? -1 : 0;
        }
        catch (System.IndexOutOfRangeException)
        {
            return 102;
        }
    }

    public static int CatchDivision(int numerator, int denominator)
    {
        try
        {
            return numerator / denominator;
        }
        catch (System.DivideByZeroException)
        {
            return 103;
        }
        catch (System.OverflowException)
        {
            return 104;
        }
    }

    public static int CatchArrayTypeMismatch(int unused)
    {
        try
        {
            object[] values = new ManagedFailure[1];
            values[0] = new Node(1);
            return -1;
        }
        catch (System.ArrayTypeMismatchException)
        {
            return unused + 105;
        }
    }

    public static int StoreNullInCovariantArray(int unused)
    {
        object[] values = new ManagedFailure[1];
        values[0] = null;
        return values[0] == null ? unused + 707 : -1;
    }

    public static int CatchStringBounds(int index)
    {
        try
        {
            string value = new string('x', 1);
            return value[index];
        }
        catch (System.IndexOutOfRangeException)
        {
            return 106;
        }
    }

    public static int CatchNegativeArrayLength(int length)
    {
        try
        {
            return new Node[length].Length;
        }
        catch (System.OverflowException)
        {
            return 107;
        }
    }

    public static int CatchNegativeStringLength(int length)
    {
        try
        {
            return new string('x', length).Length;
        }
        catch (System.ArgumentOutOfRangeException)
        {
            return 108;
        }
    }

    public static int CatchThrowNull(int unused)
    {
        try
        {
            throw null;
        }
        catch (System.NullReferenceException)
        {
            return unused + 109;
        }
    }

    public static int CatchOutOfMemory(int unused)
    {
        try
        {
            _ = new Node(1);
            return -1;
        }
        catch (System.OutOfMemoryException)
        {
            return unused + 110;
        }
    }

    public static int CatchCheckedAdd(int left, int right)
    {
        try
        {
            return checked(left + right);
        }
        catch (System.OverflowException)
        {
            return 701;
        }
    }

    public static int CatchCheckedSubtract(int left, int right)
    {
        try
        {
            return checked(left - right);
        }
        catch (System.OverflowException)
        {
            return 702;
        }
    }

    public static int CatchCheckedMultiply(int left, int right)
    {
        try
        {
            return checked(left * right);
        }
        catch (System.OverflowException)
        {
            return 703;
        }
    }

    public static int CatchInvalidCast(int invalid)
    {
        object value = new DerivedManagedFailure();
        if (invalid != 0)
        {
            value = new Node(1);
        }
        try
        {
            ManagedFailure failure = (ManagedFailure)value;
            return failure == null ? -1 : 704;
        }
        catch (System.InvalidCastException)
        {
            return 705;
        }
    }

    public static int CastNull(int unused)
    {
        object value = null;
        ManagedFailure failure = (ManagedFailure)value;
        return failure == null ? unused + 706 : -1;
    }

    public static int RunFinallyAcrossCall(int shouldThrow)
    {
        Trace = 0;
        try
        {
            InnerFinally(shouldThrow);
            return Trace;
        }
        catch (ManagedFailure error)
        {
            var handlerAllocation = new Node(100);
            return error == null ? -1 : Trace + handlerAllocation.Value;
        }
    }

    private static void InnerFinally(int shouldThrow)
    {
        try
        {
            if (shouldThrow != 0)
            {
                throw new DerivedManagedFailure();
            }
            Trace = 1;
        }
        finally
        {
            var cleanupAllocation = new Node(10);
            Trace = Trace + cleanupAllocation.Value;
        }
    }

    public static int RunRethrow(int unused)
    {
        Trace = 0;
        try
        {
            InnerRethrow();
            return -1;
        }
        catch (DerivedManagedFailure)
        {
            return Trace + unused + 200;
        }
    }

    private static void InnerRethrow()
    {
        try
        {
            throw new DerivedManagedFailure();
        }
        catch (ManagedFailure)
        {
            Trace = Trace + 1;
            throw;
        }
    }

    public static int RunCleanupReplacement(int unused)
    {
        try
        {
            InnerCleanupReplacement();
            return -1;
        }
        catch (OtherManagedFailure)
        {
            return unused + 301;
        }
        catch (DerivedManagedFailure)
        {
            return -2;
        }
    }

    private static void InnerCleanupReplacement()
    {
        try
        {
            throw new DerivedManagedFailure();
        }
        finally
        {
            throw new OtherManagedFailure();
        }
    }

    public static int RunNestedFinally(int shouldThrow)
    {
        Trace = 0;
        try
        {
            try
            {
                if (shouldThrow != 0)
                {
                    throw new DerivedManagedFailure();
                }
                Trace = 1;
            }
            finally
            {
                Trace = Trace + 10;
            }
            return Trace;
        }
        catch (ManagedFailure)
        {
            return Trace + 100;
        }
    }

    private static int Trace;
}
