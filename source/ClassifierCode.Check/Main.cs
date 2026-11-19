using System.Text;

namespace ClassifierCode.Check;

internal static class EntryPoint
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        return CheckCli.Run(args, Console.Out, Environment.CurrentDirectory);
    }
}
