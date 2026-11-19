namespace KaiAosRenew
{
    /// <summary>Snapshot / poll result from a peer relay (Firebase latestCreds).</summary>
    public sealed class PeerRelayPollResult
    {
        public bool Ok;
        public bool Unchanged;
        public long Seq;
        public PeerCredsEvent LatestCreds;
        public string ETag;
        public string Error;
    }

    /// <summary>Result of a peer relay write action (publish / ping).</summary>
    public sealed class PeerRelayActionResult
    {
        public bool Ok;
        public long Seq;
        public bool Duplicate;
        public string Error;
    }
}
