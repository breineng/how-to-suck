using UnityEngine;

namespace HowToSuck
{
    public sealed class AuthorityWorld : MonoBehaviour
    {
        public bool HasAuthority { get; private set; }
        public bool IsRunning { get; private set; }
        public ulong TickCount { get; private set; }
        public void Initialize(bool authority) { HasAuthority = authority; }
        public void SetRunning(bool running) { IsRunning = running; }
        public void Clear() { IsRunning = false; TickCount = 0; }
        private void FixedUpdate()
        {
            if (!HasAuthority || !IsRunning) return;
            TickCount++;
        }
    }
}
