using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// One process-local gate for every player-reachable developer surface. The published game keeps
    /// the console available, but F1/F5-F10, the fourth test weapon, timing capture, Sandbox entry and
    /// custom-level tooling remain inert until the trusted passphrase is entered.
    ///
    /// <para>This is an access deterrent, not remote authentication: a client binary necessarily
    /// contains the digest and can be reverse engineered. The plain passphrase is deliberately absent
    /// from source, assets, saves and build metadata.</para>
    /// </summary>
    public static class DeveloperAccess
    {
        const string UnlockDigest = "05b3b66d444b8ed761948161aa8e4b986d2df0588facc556b4c97dc60d44c286";

        static bool unlocked;

        public static bool IsUnlocked { get { return unlocked; } }
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForProcessStart()
        {
            unlocked = false;
            Changed = null;
        }

        /// <summary>Try the exact console input after whitespace/case normalization.</summary>
        public static bool TryUnlock(string candidate)
        {
            if (unlocked) return true;
            if (!DigestMatches(Digest(candidate))) return false;
            unlocked = true;
            var changed = Changed;
            if (changed != null) changed();
            return true;
        }

        static string Digest(string value)
        {
            string normalized = Normalize(value);
            if (normalized.Length == 0) return "";
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) text.Append(hash[i].ToString("x2"));
                return text.ToString();
            }
        }

        static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string[] words = value.Trim().Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words).ToLowerInvariant();
        }

        static bool DigestMatches(string candidate)
        {
            if (candidate == null || candidate.Length != UnlockDigest.Length) return false;
            int difference = 0;
            for (int i = 0; i < UnlockDigest.Length; i++)
                difference |= candidate[i] ^ UnlockDigest[i];
            return difference == 0;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Tests and automated editor probes may establish the same session capability
        /// without committing the plain passphrase to a test assembly.</summary>
        public static void UnlockForTests()
        {
            if (unlocked) return;
            unlocked = true;
            var changed = Changed;
            if (changed != null) changed();
        }

        public static void LockForTests()
        {
            unlocked = false;
            var changed = Changed;
            if (changed != null) changed();
        }
#endif
    }
}
