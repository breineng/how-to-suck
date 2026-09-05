using System.Collections.Generic;
using UnityEngine;

namespace HowToSuck
{
    public sealed class AuthorityWorld : MonoBehaviour
    {
        public bool HasAuthority { get; private set; }
        public bool IsRunning { get; private set; }
        public ulong TickCount { get; private set; }
        public int PlayerCount => players.Count;
        public IReadOnlyDictionary<int, PlayerMotor> Players => players;
        private readonly Dictionary<int, PlayerMotor> players = new Dictionary<int, PlayerMotor>();
        private readonly Dictionary<int, PlayerIntentBuffer> inputs = new Dictionary<int, PlayerIntentBuffer>();
        public void Initialize(bool authority) { HasAuthority = authority; }
        public void SetRunning(bool running) { IsRunning = running; }
        public void RegisterPlayer(PlayerMotor motor)
        {
            players.Add(motor.PlayerId, motor);
            var buffer = new PlayerIntentBuffer();
            buffer.Clear(motor.transform.eulerAngles.y, 0);
            inputs.Add(motor.PlayerId, buffer);
        }
        public void RemovePlayer(int id) { players.Remove(id); inputs.Remove(id); }
        public bool SubmitIntent(int id, PlayerIntent intent)
        {
            return HasAuthority && IsRunning && inputs.TryGetValue(id, out var buffer)
                && buffer.TrySubmit(intent, Time.realtimeSinceStartupAsDouble);
        }
        public void Clear()
        {
            IsRunning = false; TickCount = 0; players.Clear(); inputs.Clear();
        }
        private void FixedUpdate()
        {
            if (!HasAuthority || !IsRunning) return;
            TickCount++;
            double now = Time.realtimeSinceStartupAsDouble;
            foreach (var pair in players)
                if (pair.Value != null) pair.Value.Step(inputs[pair.Key].Read(now), Time.fixedDeltaTime);
        }
    }
}
