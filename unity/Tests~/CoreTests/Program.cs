// dotnet run -- unit            unit tests (no network)
// dotnet run -- live            live checks against production (needs LOKUTOR_API_KEY)
// dotnet run -- demo [outdir]   a scripted conversation with a game character, written to a WAV
using System;
using System.Threading.Tasks;

static class Program
{
    static async Task<int> Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "unit";
        switch (mode)
        {
            case "unit":
                return UnitTests.RunAll() ? 0 : 1;
            case "live":
                return await LiveTests.RunAll() ? 0 : 1;
            case "demo":
                return await NpcDemo.Run(args.Length > 1 ? args[1] : "demo-output") ? 0 : 1;
            default:
                Console.WriteLine("usage: unit | live | demo [outdir]");
                return 2;
        }
    }
}
