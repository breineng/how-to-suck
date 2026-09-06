using System;
using System.Collections.Generic;
namespace HowToSuck
{
    public enum PreparationStatus { Waiting, Ready, Committed, Cancelled, TimedOut }
    // Host-side readiness bookkeeping only. It never starts a controller, moves bodies or applies money.
    public sealed class RunPreparationBarrier
    {
        private sealed class Member { public bool Scene, Assigned, Ack; public ulong PlayerObject; }
        private readonly Dictionary<ulong, Member> members = new Dictionary<ulong, Member>();
        public readonly string RunId;
        public readonly uint WorldRevision;
        public readonly int ExpectedItems, ExpectedPlayers;
        public readonly double StartedAt, Deadline;
        public PreparationStatus Status { get; private set; } = PreparationStatus.Waiting;
        public string Failure { get; private set; }
        public RunPreparationBarrier(string runId, IEnumerable<ulong> roster, uint worldRevision, int itemCount, double now)
        {
            if (!Guid.TryParseExact(runId, "N", out _) || roster == null || worldRevision == 0 || itemCount < 0 || !Finite(now))
                throw new ArgumentException("A prepared run, exact roster/revision, item count and monotonic time are required.");
            foreach (ulong id in roster)
                if (members.ContainsKey(id)) throw new ArgumentException("Duplicate connection in prepared roster.");
                else members.Add(id, new Member());
            if (members.Count < 1 || members.Count > 4) throw new ArgumentException("Prepared roster must contain one to four connections.");
            RunId = runId; WorldRevision = worldRevision; ExpectedItems = itemCount; ExpectedPlayers = members.Count;
            StartedAt = now; Deadline = now + 30;
            if (!Finite(Deadline)) throw new ArgumentException("Preparation deadline overflow.");
        }
        private bool Open => Status == PreparationStatus.Waiting || Status == PreparationStatus.Ready;
        public bool RecordSceneLoaded(ulong id)
        {
            if (!Open || !members.TryGetValue(id, out var member)) return false;
            member.Scene = true; return true;
        }
        public bool AssignPlayer(ulong id, ulong networkObjectId)
        {
            if (!Open || !members.TryGetValue(id, out var member) || !member.Scene || member.Assigned) return false;
            foreach (var peer in members.Values) if (peer.Assigned && peer.PlayerObject == networkObjectId) return false;
            member.Assigned = true; member.PlayerObject = networkObjectId; return true; // NGO object ID zero is valid.
        }
        // The caller passes the actual RPC sender. Claimed sender/player identity is never used.
        public bool Acknowledge(ulong sender, string runId, uint revision, ulong ownedPlayerObject, int receivedItems, int receivedPlayers)
        {
            if (!Open || runId != RunId || revision != WorldRevision || receivedItems != ExpectedItems || receivedPlayers != ExpectedPlayers ||
                !members.TryGetValue(sender, out var member) || !member.Scene || !member.Assigned || member.PlayerObject != ownedPlayerObject || member.Ack) return false;
            member.Ack = true; return true;
        }
        public PreparationStatus Poll(double now, IEnumerable<ulong> connected)
        {
            if (!Open) return Status;
            if (!Finite(now) || now < StartedAt) return Cancel("Authority clock is invalid.");
            // Deadline wins even if the last acknowledgment arrives on this boundary.
            if (now >= Deadline) { Failure = "Preparation exceeded thirty seconds."; return Status = PreparationStatus.TimedOut; }
            if (connected == null) return Cancel("Prepared roster is unavailable.");
            var actual = new HashSet<ulong>();
            foreach (ulong id in connected) if (!actual.Add(id)) return Cancel("Duplicate live connection.");
            if (!actual.SetEquals(members.Keys)) return Cancel("Prepared roster changed.");
            foreach (var member in members.Values) if (!member.Scene || !member.Assigned || !member.Ack) return Status = PreparationStatus.Waiting;
            return Status = PreparationStatus.Ready;
        }
        public bool TryCommit(double now, IEnumerable<ulong> connected)
        {
            if (Poll(now, connected) != PreparationStatus.Ready) return false;
            Status = PreparationStatus.Committed; return true;
        }
        public PreparationStatus Cancel(string reason)
        {
            if (Status == PreparationStatus.Committed) return Status;
            Failure = reason; return Status = PreparationStatus.Cancelled;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
