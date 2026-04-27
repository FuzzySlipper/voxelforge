namespace VoxelForge.Import;

public static class Program
{
    public static int Main(string[] args)
    {
        var cli = new ImportCli();
        return cli.Execute(args, Console.Out, Console.Error);
    }
}
