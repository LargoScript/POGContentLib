using System;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace POGContentLib.Core
{
    /// <summary>
    /// Reflective bridge to the current Steam lobby's metadata, used to advertise/read the parity
    /// manifest. Deliberately NOT compile-time bound to the Facepunch interop: the exact interop
    /// shape (Nullable&lt;Lobby&gt; projection, struct-vs-class for Lobby) is a runtime unknown, so
    /// every access is name-resolved and guarded — a shape change surfaces as one log line, never a
    /// crash. The whole class is best-effort; the parity COMPARISON logic lives (and is testable) in
    /// <see cref="ParityManifest"/>, independent of anything here.
    ///
    /// RUNTIME-TODO (validate in a 2-player game):
    ///   • that NetworkHandler.Singleton.Lobby is populated at the moment we advertise/read
    ///     (host: after CreateLobbyAsync; client: after join, before the NGO prefab-set check);
    ///   • that lobby metadata written by the host is visible to a joining client at that time.
    /// </summary>
    internal static class ParitySteamBridge
    {
        private const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static bool _warnedUnavailable;

        /// <summary>Write a key/value onto the current lobby. Returns false if there is no lobby yet.</summary>
        public static bool TrySetLobbyData(string key, string value)
        {
            try
            {
                object lobby = GetCurrentLobby();
                if (lobby == null) return false;
                var setData = lobby.GetType().GetMethod(GameNames.Steam.Lobby_SetData, Flags,
                    null, new[] { typeof(string), typeof(string) }, null);
                if (setData == null) { WarnOnce("Lobby.SetData(string,string) not found on this build."); return false; }
                setData.Invoke(lobby, new object[] { key, value });
                return true;
            }
            catch (Exception ex)
            {
                WarnOnce($"TrySetLobbyData failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Read a key from the current lobby. Returns null if no lobby; "" if the key is unset.</summary>
        public static string TryGetLobbyData(string key)
        {
            try
            {
                object lobby = GetCurrentLobby();
                if (lobby == null) return null;
                var getData = lobby.GetType().GetMethod(GameNames.Steam.Lobby_GetData, Flags,
                    null, new[] { typeof(string) }, null);
                if (getData == null) { WarnOnce("Lobby.GetData(string) not found on this build."); return null; }
                return getData.Invoke(lobby, new object[] { key }) as string;
            }
            catch (Exception ex)
            {
                WarnOnce($"TryGetLobbyData failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolve the live <c>Lobby</c> via <c>NetworkHandler.Singleton.Lobby</c> (a Nullable&lt;Lobby&gt;),
        /// returning the unwrapped Lobby object, or null if there is no current lobby.
        /// </summary>
        private static object GetCurrentLobby()
        {
            Type nhType = FindType(GameNames.Steam.NetworkHandlerTypeFullName);
            if (nhType == null) { WarnOnce($"Type not found: {GameNames.Steam.NetworkHandlerTypeFullName}"); return null; }

            object singleton = GetMemberValue(nhType, null, GameNames.Steam.NetworkHandler_Singleton);
            if (singleton == null) return null; // no active NetworkHandler (not in a session)

            object lobbyNullable = GetMemberValue(nhType, singleton, GameNames.Steam.NetworkHandler_Lobby);
            return Unwrap(lobbyNullable);
        }

        /// <summary>Read a property (or its get_ method / field) by name, static when instance is null.</summary>
        private static object GetMemberValue(Type type, object instance, string name)
        {
            var prop = type.GetProperty(name, Flags);
            if (prop != null && prop.CanRead) return prop.GetValue(instance);
            var getter = type.GetMethod("get_" + name, Flags, null, Type.EmptyTypes, null);
            if (getter != null) return getter.Invoke(instance, null);
            var field = type.GetField(name, Flags);
            if (field != null) return field.GetValue(instance);
            return null;
        }

        /// <summary>Unwrap a Nullable&lt;Lobby&gt; (or a projected optional) to the Lobby, or pass through.</summary>
        private static object Unwrap(object nullable)
        {
            if (nullable == null) return null;
            Type t = nullable.GetType();
            var hasValue = t.GetProperty("HasValue", Flags);
            if (hasValue != null)
            {
                if (!(bool)hasValue.GetValue(nullable)) return null;
                var valueProp = t.GetProperty("Value", Flags);
                return valueProp != null ? valueProp.GetValue(nullable) : nullable;
            }
            return nullable; // already the Lobby (interop projected it as a plain struct/class)
        }

        /// <summary>Find a loaded type by full name across all assemblies (interop names are stable).</summary>
        private static Type FindType(string fullName)
        {
            var direct = Type.GetType(fullName, throwOnError: false);
            if (direct != null) return direct;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, throwOnError: false); }
                catch { /* dynamic/reflection-only assemblies can throw */ }
                if (t != null) return t;
            }
            return null;
        }

        private static void WarnOnce(string msg)
        {
            if (_warnedUnavailable) return;
            _warnedUnavailable = true;
            MelonLogger.Warning($"[POGContentLib] Parity/Steam bridge unavailable — {msg} " +
                "Parity detection via lobby metadata is disabled this session (the game's own " +
                "connect-time prefab check still applies). This is a RUNTIME-TODO for Milestone 0.");
        }
    }
}
