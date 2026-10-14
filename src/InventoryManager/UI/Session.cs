using System;
using System.Security.Cryptography;
using System.Text;
using InventoryManager.Data;

namespace InventoryManager.UI
{
    /// <summary>
    /// App lock. With no PIN set the app is always open (IsOwner = true).
    /// Once a PIN exists the app starts locked; MainForm then covers the whole window with the
    /// lock screen until the PIN is entered, and locks again on request or after 10 idle minutes.
    /// </summary>
    public static class Session
    {
        private static bool _unlocked;

        /// <summary>Last user interaction, used by the auto-relock timer in MainForm.</summary>
        public static DateTime LastActivity = DateTime.Now;

        public static event EventHandler Changed;

        public static bool PinEnabled
        {
            get { return !string.IsNullOrEmpty(AppSettings.OwnerPinHash); }
        }

        public static bool IsOwner
        {
            get { return !PinEnabled || _unlocked; }
        }

        public static bool Unlock(string pin)
        {
            if (!Verify(pin)) return false;
            _unlocked = true;
            OnChanged();
            return true;
        }

        public static void Lock()
        {
            if (!PinEnabled) return;
            _unlocked = false;
            OnChanged();
        }

        public static bool Verify(string pin)
        {
            if (!PinEnabled) return true;
            return Hash(AppSettings.OwnerPinSalt, pin) == AppSettings.OwnerPinHash;
        }

        public static void SetPin(string pin)
        {
            var saltBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(saltBytes);
            AppSettings.OwnerPinSalt = Convert.ToBase64String(saltBytes);
            AppSettings.OwnerPinHash = Hash(AppSettings.OwnerPinSalt, pin);
            AppSettings.Save();
            _unlocked = true;
            OnChanged();
        }

        public static void RemovePin()
        {
            AppSettings.OwnerPinSalt = "";
            AppSettings.OwnerPinHash = "";
            AppSettings.Save();
            _unlocked = false;
            OnChanged();
        }

        /// <summary>Normalizes Bangla digits, then salted SHA-256.</summary>
        public static string Hash(string salt, string pin)
        {
            var text = salt + "|" + L.ToAscii((pin ?? "").Trim());
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        private static void OnChanged()
        {
            var handler = Changed;
            if (handler != null) handler(null, EventArgs.Empty);
        }
    }
}
