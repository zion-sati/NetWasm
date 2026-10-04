namespace NetWasm.Wit.Netwasm.Worker.Template._1._0._0;

public static partial class ApplicationExports
{
    public static partial uint Run(uint total)
    {
        for (var completed = 0u; completed < total;)
        {
            completed++;
            NotificationsImports.Report(completed, total);
        }
        return total;
    }
}
