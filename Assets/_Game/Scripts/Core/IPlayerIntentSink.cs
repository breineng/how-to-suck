namespace HowToSuck
{
    public interface IPlayerIntentSink
    {
        void SubmitIntent(int playerId, PlayerIntent intent);
    }
}
