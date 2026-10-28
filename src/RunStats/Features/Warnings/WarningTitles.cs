namespace RunStats.Features.Warnings;

public static class WarningTitles
{
    public static string Title(WarningKind kind) => kind switch
    {
        WarningKind.FastJump => "Fast running",
        WarningKind.VeryFastJump => "Very fast running",
        WarningKind.VolumeJump => "More running",
        WarningKind.LongRunJump => "Longer run",
        WarningKind.HardRunsBackToBack => "Hard runs back to back",
        WarningKind.FormChange => "Form change",
        WarningKind.BalanceShift => "Balance shift",
        _ => kind.ToString()
    };
}
