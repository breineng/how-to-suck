using System;

namespace HowToSuck
{
    // Process memory only in task 06. No disk, purchase, or Steam implementation.
    public sealed class CampaignState
    {
        public string CampaignId { get; }
        public string CurrentTierId { get; }
        public long Balance { get; private set; }
        public string LastSettledRunId { get; private set; }

        public CampaignState(string campaignId, string currentTierId = "mk1",
            long balance = 0, string lastSettledRunId = null)
        {
            if (string.IsNullOrWhiteSpace(campaignId) || campaignId != campaignId.Trim())
                throw new ArgumentException("A stable campaign ID is required.", nameof(campaignId));
            if (string.IsNullOrWhiteSpace(currentTierId))
                throw new ArgumentException("An equipment tier is required.", nameof(currentTierId));
            if (balance < 0) throw new ArgumentOutOfRangeException(nameof(balance));
            if (lastSettledRunId != null && string.IsNullOrWhiteSpace(lastSettledRunId))
                throw new ArgumentException("An existing settlement ID cannot be empty.", nameof(lastSettledRunId));
            CampaignId = campaignId; CurrentTierId = currentTierId;
            Balance = balance; LastSettledRunId = lastSettledRunId;
        }

        internal void CommitSettlement(long balance, string runId)
        {
            if (balance < 0) throw new ArgumentOutOfRangeException(nameof(balance));
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException(nameof(runId));
            Balance = balance;
            LastSettledRunId = runId;
        }
    }
}
