namespace HowToSuck
{
    public static class NetworkIntentCopy
    {
        public static bool FireSuppressed(PlayerIntent value)=>value.SuppressFire;
        public static PlayerIntent WithFireSuppression(PlayerIntent value,bool suppressed)
        {value.SuppressFire=suppressed;return value;}
        public static bool JumpSuppressed(PlayerIntent value)=>value.SuppressJump;
        public static PlayerIntent WithJumpSuppression(PlayerIntent value,bool suppressed)
        {value.SuppressJump=suppressed;return value;}
    }
}
