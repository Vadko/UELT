using System.Text;
using UELT.Cli.Commands;
using UELT.Cli.Infra;
using UELT.Hikaro;

namespace UELT.Cli;

public sealed record Command(string Name, string Summary, Opt[] Options, Func<ParsedArgs, int> Run);

public static class Program
{
    public static readonly Command[] Commands =
    {
        UnpackCommand.Command,
        PackCommand.Command,
        InfoCommand.Command,
        StatsCommand.Command,
        SearchCommand.Command,
        ReplaceCommand.Command,
        CheckCommand.Command,
        TmCommand.Command,
        GlossaryCommand.Command,
        LocresCommands.CreateLocres,
        LocresCommands.MergeLocres,
        LocresCommands.AddRows,
        LocresCommands.RemoveRows,
        LocresCommands.ImportHashes,
        LocresCommands.CompareLocres,
    };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);

        try
        {
            return Dispatch(args);
        }
        catch (UsageException ex)
        {
            Log.Error(ex.Message);
            Log.Raw($"Довідка: {AppVersion.Name.ToLowerInvariant()}-cli help{(args.Length > 0 && Find(args[0]) != null ? " " + args[0] : "")}");
            return ExitCodes.Usage;
        }
        catch (NeedsEngineException ex)
        {
            Log.Error(ex.Message);
            return ExitCodes.NeedsEngine;
        }
        catch (Exception ex)
        {
            Log.Error(BatchRunner.Describe(ex));
            return ExitCodes.Error;
        }
        finally
        {
            Console.Out.Flush();
        }
    }

    private static Command Find(string name)
        => Commands.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private static int Dispatch(string[] args)
    {
        if (args.Length == 0)
        {
            Log.Raw(HelpText.General());
            return ExitCodes.Usage;
        }

        string first = args[0];

        if (first is "--version" or "-v" or "version")
        {
            Log.Out($"uelt-cli {AppVersion.Version}");
            return ExitCodes.Ok;
        }

        if (first is "--help" or "-h" or "-?" or "help")
        {
            if (first == "help" && args.Length > 1)
            {
                var c = Find(args[1]) ?? throw new UsageException($"Невідома команда: {args[1]}");
                Log.Out(HelpText.For(c.Name));
                return ExitCodes.Ok;
            }
            Log.Out(HelpText.General());
            return ExitCodes.Ok;
        }

        var cmd = Find(first) ?? throw new UsageException($"Невідома команда: {first}");
        var rest = args.Skip(1).ToList();

        // "<command> --help" works even when other arguments are incomplete.
        if (rest.Any(a => a is "--help" or "-h" or "-?"))
        {
            Log.Out(HelpText.For(cmd.Name));
            return ExitCodes.Ok;
        }

        var parsed = ParsedArgs.Parse(rest, cmd.Options);
        Log.Quiet = parsed.Has("--quiet");
        return cmd.Run(parsed);
    }
}
