// From the compiler harness's DesignCommandTransactionCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel.Design;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
namespace DesignFixture;
sealed class Command : CommandID
{
    public Command():base(Guid.Empty,0){}
    public override int ID=>42;
    public override Guid Guid=>new Guid("00112233-4455-6677-8899-aabbccddeeff");
}
sealed class Transaction : DesignerTransaction
{
    public int Commits, Cancels, Disposals;public bool Throw, SeenState, Reenter, LastDisposing;
    public Transaction(){}
    public Transaction(string description):base(description){}
    protected override void OnCommit(){Commits++;SeenState=Committed&&!Canceled;if(Reenter){Cancel();Commit();}if(Throw)throw new InvalidOperationException();}
    protected override void OnCancel(){Cancels++;SeenState=Canceled&&!Committed;if(Reenter){Commit();Cancel();}if(Throw)throw new InvalidOperationException();}
    protected override void Dispose(bool disposing){Disposals++;LastDisposing=disposing;base.Dispose(disposing);}
    public void DisposeFalse()=>Dispose(false);
}
public static class EntryPoint
{
    public static int Run(int input)
    {
        var guid=new Guid("00112233-4455-6677-8899-aabbccddeeff");var a=new CommandID(guid,42);var b=new CommandID(guid,42);
        if(a.ID!=42 || a.Guid!=guid || !a.Equals(b) || !a.Equals(new Command()) || a.GetHashCode()!=b.GetHashCode() || a.Equals(null) || a.Equals(new object()) || a.Equals(new CommandID(guid,43)) || a.Equals(new CommandID(Guid.Empty,42)))return 1;
        if(a.ToString()!="00112233-4455-6677-8899-aabbccddeeff : 42")return 2;
        var commit=new Transaction("change"){Reenter=true};if(commit.Description!="change" || commit.Committed || commit.Canceled)return 3;
        commit.Commit();commit.Commit();commit.Cancel();((IDisposable)commit).Dispose();
        if(!commit.Committed || commit.Canceled || commit.Commits!=1 || commit.Cancels!=0 || !commit.SeenState || !commit.LastDisposing)return 4;
        var cancel=new Transaction(){Reenter=true};if(cancel.Description!="")return 5;
        cancel.Cancel();cancel.Commit();cancel.Cancel();if(!cancel.Canceled || cancel.Committed || cancel.Cancels!=1 || cancel.Commits!=0 || !cancel.SeenState)return 6;
        var disposed=new Transaction();((IDisposable)disposed).Dispose();((IDisposable)disposed).Dispose();if(disposed.Disposals!=2 || disposed.Cancels!=1 || !disposed.Canceled)return 7;
        var fallback=new Transaction((string)null);if(fallback.Description!=null)return 8;fallback.DisposeFalse();if(fallback.LastDisposing || fallback.Cancels!=1)return 9;
        var failedCommit=new Transaction(){Throw=true};try{failedCommit.Commit();return 10;}catch(InvalidOperationException){}failedCommit.Commit();failedCommit.Cancel();if(!failedCommit.Committed || failedCommit.Commits!=1 || failedCommit.Cancels!=0)return 11;
        var failedCancel=new Transaction(){Throw=true};try{failedCancel.Cancel();return 12;}catch(InvalidOperationException){}failedCancel.Cancel();failedCancel.Commit();if(!failedCancel.Canceled || failedCancel.Cancels!=1 || failedCancel.Commits!=0)return 13;
        return 42;
    }
}
