namespace UELT.Cli.Infra;

/// <summary>The file is intentionally not processed (e.g. no texts). Single file: exit 1; batch: counted as skipped.</summary>
public sealed class SkipException(string message) : Exception(message);

/// <summary>No matching source file was found for this input (GUI: "Не знайдено відповідний файл").</summary>
public sealed class UnmatchedException(string message) : Exception(message);

/// <summary>Runs an action per input file; in a batch, per-file errors are reported and the batch continues.</summary>
public sealed class BatchRunner(bool isBatch)
{
    public bool IsBatch { get; } = isBatch;
    public int Processed { get; private set; }
    public int Ok { get; private set; }
    public int Failed { get; private set; }
    public int Skipped { get; private set; }
    public int Unmatched { get; private set; }
    public int NeedsEngine { get; private set; }
    public List<string> Errors { get; } = new();

    public void Run(IEnumerable<InputFile> files, Action<InputFile> action)
    {
        foreach (var f in files)
        {
            Processed++;
            try
            {
                action(f);
                Ok++;
            }
            catch (UsageException) { throw; }
            catch (SkipException ex)
            {
                Skipped++;
                if (IsBatch) Log.Info($"Пропущено {f.Display}: {ex.Message}");
                else Log.Error($"{f.Display}: {ex.Message}");
            }
            catch (UnmatchedException ex)
            {
                Unmatched++;
                Log.Warn($"{f.Display}: {ex.Message}");
            }
            catch (NeedsEngineException ex)
            {
                NeedsEngine++;
                Errors.Add($"{f.Display}: {ex.Message}");
                Log.Error(ex.Message);
            }
            catch (Exception ex)
            {
                Failed++;
                string msg = Describe(ex);
                Errors.Add($"{f.Display}: {msg}");
                Log.Error($"{f.Display}: {msg}");
            }
        }
    }

    public static string Describe(Exception ex) => ErrorText.Describe(ex);

    public int ExitCode()
    {
        if (NeedsEngine > 0) return ExitCodes.NeedsEngine;
        if (IsBatch)
        {
            if (Failed > 0 || Unmatched > 0) return ExitCodes.Partial;
            // Nothing at all was processed (e.g. every asset had no texts): not a success.
            if (Ok == 0 && Processed > 0) return ExitCodes.Partial;
            return ExitCodes.Ok;
        }
        if (Failed > 0 || Skipped > 0) return ExitCodes.Error;
        if (Unmatched > 0) return ExitCodes.Partial;
        return ExitCodes.Ok;
    }
}
