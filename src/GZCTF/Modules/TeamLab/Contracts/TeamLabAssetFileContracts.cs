namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabAssetFileCommand(int Generation, string Operation, string Path,
    byte[]? Content = null, bool Overwrite = false, bool Confirmed = false);
