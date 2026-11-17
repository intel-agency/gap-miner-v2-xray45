namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// The documented <c>ActorJobStatus</c> values an Apify actor run can report
/// (Apify API v2 reference, "Get Actor run"). Kept as constants rather than an
/// enum so an undocumented status added by Apify still deserializes instead of
/// failing the client — <see cref="ApifyActorRun.Status"/> is the raw wire
/// string, and consumers compare against these constants.
/// </summary>
public static class ApifyRunStatus
{
    /// <summary>The run was created but has not started yet.</summary>
    public const string Ready = "READY";

    /// <summary>The run is currently executing.</summary>
    public const string Running = "RUNNING";

    /// <summary>The run finished successfully; its dataset holds the output items.</summary>
    public const string Succeeded = "SUCCEEDED";

    /// <summary>The run failed; see <see cref="ApifyActorRun.StatusMessage"/> and exit code.</summary>
    public const string Failed = "FAILED";

    /// <summary>The run is about to exceed its timeout.</summary>
    public const string TimingOut = "TIMING-OUT";

    /// <summary>The run exceeded its timeout.</summary>
    public const string TimedOut = "TIMED-OUT";

    /// <summary>The run is being aborted.</summary>
    public const string Aborting = "ABORTING";

    /// <summary>The run was aborted.</summary>
    public const string Aborted = "ABORTED";
}
