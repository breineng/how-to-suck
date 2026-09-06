namespace HowToSuck
{
    public static class NetworkIntentCopy
    {
        public static bool JumpSuppressed(PlayerIntent value)=>value.SuppressJump;
        public static PlayerIntent WithJumpSuppression(PlayerIntent value,bool suppressed)
        {value.SuppressJump=suppressed;return value;}
    }
}
