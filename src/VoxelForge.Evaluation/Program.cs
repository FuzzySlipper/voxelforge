namespace VoxelForge.Evaluation;

public static class Program
{
    public static int Main(string[] args)
    {
        var cli = new BenchmarkCli();
        return cli.Execute(args, Console.Out, Console.Error);
    }
}
