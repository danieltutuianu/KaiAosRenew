using System.Collections.Generic;

namespace KaiAosRenew
{
    /// <summary>Snapshot / poll result from a peer relay (Firebase).</summary>
    public sealed class PeerRelayPollResult
    {
        public bool Ok;
        public bool Unchanged;
        public long Seq;
        public long AckSeq;
        public long CmdSeq;
        public long PeersSeq;
        public PeerCredsEvent LatestCreds;
        public PeerCommandEvent LatestCommand;
        public List<PeerAckEvent> Acks = new List<PeerAckEvent>();
        public List<PeerRecord> Peers = new List<PeerRecord>();
        public string Error;
    }

    /// <summary>Result of a peer relay write action (publish / ack / heartbeat / command / ping).</summary>
    public sealed class PeerRelayActionResult
    {
        public bool Ok;
        public long Seq;
        public long AckSeq;
        public long CmdSeq;
        public long PeersSeq;
        public bool Duplicate;
        public string Error;
    }
}
