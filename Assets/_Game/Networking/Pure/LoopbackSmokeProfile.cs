using System;
using System.Collections.Generic;
namespace HowToSuck.Networking
{
    // Diagnostic routing only. Never used by the product's solo or Steam menu.
    public sealed class LoopbackSmokeProfile
    {
        public const ushort Port = 17841;
        public readonly int Slot, PlayerCount;
        public readonly string Nonce;
        public bool IsHost => Slot == 0;
        private LoopbackSmokeProfile(int slot, int players, string nonce)
        { Slot = slot; PlayerCount = players; Nonce = nonce; }
        public static LoopbackSmokeProfile Create(int slot, int players, string nonce, bool development)
        {
            if (!development) throw new InvalidOperationException("Loopback multiplayer smoke is disabled outside development.");
            if ((players != 2 && players != 4) || slot < 0 || slot >= players) throw new ArgumentException("Expected host+1 or host+3 distinct slots.");
            if (!Guid.TryParseExact(nonce, "N", out _)) throw new ArgumentException("Use one shared 32-hex scenario nonce.");
            return new LoopbackSmokeProfile(slot, players, nonce.ToLowerInvariant());
        }
        public static LoopbackSmokeProfile FromTags(IEnumerable<string> tags, bool mainEditor, string nonce)
        {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            var found = new HashSet<string>(tags, StringComparer.Ordinal);
            // MPPM2.0 scenario UI assigns one tag per player. Every accepted compound tag explicitly
            // carries enable + count + role; no default or fuzzy string parsing is allowed.
            string[] compound = { "HTS-NET-SMOKE-2-HOST", "HTS-NET-SMOKE-2-CLIENT-1",
                "HTS-NET-SMOKE-4-HOST", "HTS-NET-SMOKE-4-CLIENT-1", "HTS-NET-SMOKE-4-CLIENT-2", "HTS-NET-SMOKE-4-CLIENT-3" };
            int matched = -1, matches = 0;
            foreach (var tag in found)
            {
                int index = Array.IndexOf(compound, tag);
                if (index >= 0) { matched = index; matches++; }
                else if (tag != null && tag.StartsWith("HTS-NET-SMOKE-", StringComparison.Ordinal))
                    throw new ArgumentException("Unknown compound diagnostic tag.");
            }
            if (matches > 0)
            {
                foreach (var tag in found)
                    if (tag == "HTS-NET-SMOKE" || tag == "HTS-HOST" ||
                        tag != null && (tag.StartsWith("HTS-CLIENT-", StringComparison.Ordinal) || tag.StartsWith("HTS-PLAYERS-", StringComparison.Ordinal)))
                        throw new ArgumentException("Do not mix compound UI and separate API role tags.");
                if (matches != 1) throw new ArgumentException("Exactly one compound diagnostic tag is required.");
                int compoundSlot = matched < 2 ? matched : matched - 2;
                if ((compoundSlot == 0) != mainEditor) throw new ArgumentException("Main editor hosts; additional editors are clients.");
                return Create(compoundSlot, matched < 2 ? 2 : 4, nonce, true);
            }
            if (!found.Contains("HTS-NET-SMOKE")) throw new InvalidOperationException("The explicit diagnostic enable tag is absent.");
            int slot = -1, roles = 0;
            string[] rolesBySlot = { "HTS-HOST", "HTS-CLIENT-1", "HTS-CLIENT-2", "HTS-CLIENT-3" };
            for (int i = 0; i < rolesBySlot.Length; i++) if (found.Contains(rolesBySlot[i])) { slot = i; roles++; }
            bool two = found.Contains("HTS-PLAYERS-2"), four = found.Contains("HTS-PLAYERS-4");
            if (roles != 1 || two == four) throw new ArgumentException("Assign exactly one role and exactly one player-count tag.");
            if ((slot == 0) != mainEditor) throw new ArgumentException("This scenario requires the main editor host and additional-editor clients.");
            return Create(slot, two ? 2 : 4, nonce, true);
        }
    }
}
