using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ScheduledMessages
{
    public class ScheduledMessage
    {
        public int    Hour;
        public int    Minute;
        public string Time;
        public string Message;
    }

    public class ScheduledMessagesConfig
    {
        public int                    UtcOffset;
        public string                 WelcomeMessage;
        public int                    WelcomeDelay;
        public List<ScheduledMessage> ScheduledMessages = new List<ScheduledMessage>();
    }

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class ScheduledMessagesPlugin : BaseUnityPlugin
    {
        public const string PluginGUID    = "com.yourname.scheduledmessages";
        public const string PluginName    = "ScheduledMessages";
        public const string PluginVersion = "1.5.0";

        internal static ManualLogSource Log;
        private Harmony harmony;

        private ScheduledMessagesConfig config;

        private readonly HashSet<string> sentThisMinute = new HashSet<string>();
        private int lastCheckedMinute = -1;

        private readonly HashSet<long> knownPeers = new HashSet<long>();

        private string configPath;
        private FileSystemWatcher configWatcher;
        private DateTime lastConfigReload = DateTime.MinValue;

        private void Awake()
        {
            Log = Logger;
            harmony = new Harmony(PluginGUID);
            harmony.PatchAll();

            configPath = Path.Combine(Paths.ConfigPath, "scheduledmessages.cfg");

            LoadConfig();
            StartConfigWatcher();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        private void LoadConfig()
        {
            if (!File.Exists(configPath))
            {
                Log.LogError($"Config not found at {configPath}. No welcome or scheduled messages will be sent.");
                return;
            }

            var newConfig = new ScheduledMessagesConfig();

            foreach (string raw in File.ReadAllLines(configPath))
            {
                string line = raw.Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                if (line.StartsWith("timezone="))
                {
                    int.TryParse(line.Substring("timezone=".Length).Trim(), out newConfig.UtcOffset);
                }
                else if (line.StartsWith("welcome="))
                {
                    newConfig.WelcomeMessage = line.Substring("welcome=".Length).Trim();
                }
                else if (line.StartsWith("welcome-delay="))
                {
                    int.TryParse(line.Substring("welcome-delay=".Length).Trim(), out newConfig.WelcomeDelay);
                }
                else
                {
                    int space = line.IndexOf(' ');
                    if (space < 0) continue;

                    string time = line.Substring(0, space).Trim();
                    string text = line.Substring(space + 1).Trim();

                    if (TryParseTime(time, out int hour, out int minute) && !string.IsNullOrEmpty(text))
                    {
                        newConfig.ScheduledMessages.Add(new ScheduledMessage { Hour = hour, Minute = minute, Time = time, Message = text });
                    }
                }
            }

            config = newConfig;

            LogConfig();
        }

        private void LogConfig()
        {
            Log.LogInfo($"Config loaded: timezone={config.UtcOffset}");
            Log.LogInfo($"Config loaded: welcome={config.WelcomeMessage}");
            Log.LogInfo($"Config loaded: welcome-delay={config.WelcomeDelay}");

            foreach(var msg in config.ScheduledMessages)
            {
                Log.LogInfo($"Config loaded: {msg.Time} {msg.Message}");
            }
        }

        private void StartConfigWatcher()
        {
            configWatcher = new FileSystemWatcher(Paths.ConfigPath, "scheduledmessages.cfg");
            configWatcher.NotifyFilter = NotifyFilters.LastWrite;
            configWatcher.Changed += OnConfigChanged;
            configWatcher.EnableRaisingEvents = true;
        }

        private void OnConfigChanged(object sender, FileSystemEventArgs e)
        {
            // Debounce this event being fired multiple times per file update
            // Apparently operating systems like to touch a file multiple times - once for the content edit, then again for the metadata update.
            if ((DateTime.Now - lastConfigReload).TotalSeconds < 1) return;
            lastConfigReload = DateTime.Now;

            System.Threading.Thread.Sleep(200);
            Log.LogInfo("Config file changed, reloading...");

            LoadConfig();
        }

        private void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            if (config.WelcomeMessage != "")
            {
                CheckForNewPeers();
            }

            if (!config.ScheduledMessages.Any()) return;

            DateTime now = DateTime.UtcNow.AddHours(config.UtcOffset);
            int currentMinute = now.Hour * 60 + now.Minute;

            if (currentMinute != lastCheckedMinute)
            {
                sentThisMinute.Clear();
                lastCheckedMinute = currentMinute;
            }

            foreach (var entry in config.ScheduledMessages)
            {
                int scheduledMinute = entry.Hour * 60 + entry.Minute;
                string key = entry.Time + "|" + entry.Message;

                if (currentMinute == scheduledMinute && !sentThisMinute.Contains(key))
                {
                    Broadcast(entry.Message);
                    sentThisMinute.Add(key);
                }
            }
        }

        private void CheckForNewPeers()
        {
            if (string.IsNullOrEmpty(config.WelcomeMessage)) return;

            var peers = ZNet.instance.GetPeers();

            foreach (var peer in peers)
            {
                if (peer.m_uid == 0) continue;  // ignore ghost peers

                if (!knownPeers.Contains(peer.m_uid))
                {
                    knownPeers.Add(peer.m_uid);
                    Log.LogInfo($"New peer connected: {peer.m_playerName} ({peer.m_uid}), sending welcome message in {config.WelcomeDelay} seconds.");
                    StartCoroutine(SendDelayedWelcome(peer));
                }
            }

            // Clean up peers that have disconnected
            var currentUids = new HashSet<long>(peers.Select(p => p.m_uid));
            knownPeers.RemoveWhere(uid => !currentUids.Contains(uid));
        }

        private IEnumerator SendDelayedWelcome(ZNetPeer peer)
        {
            yield return new WaitForSeconds(config.WelcomeDelay);

            if (peer == null)
            {
                Log.LogInfo($"[Welcome] no peer found to send welcome to.");
                yield break;
            }

            RpcChatMessage(peer, Talker.Type.Normal, config.WelcomeMessage);

            Log.LogInfo($"[Welcome] Sent to {peer.m_playerName}: {config.WelcomeMessage}");
        }

        private void Broadcast(string text)
        {
            var peers = ZNet.instance.GetPeers();

            if (!peers.Any())
            {
                Log.LogInfo($"[Scheduled] No peers connected, skipping: {text}");
                return;
            }

            foreach (var peer in peers)
            {
                RpcChatMessage(peer, Talker.Type.Normal, text);
            }

            Log.LogInfo($"[Scheduled] {text}");
        }

        private void RpcChatMessage(ZNetPeer peer, Talker.Type talkerType, string text)
        {

            ZRoutedRpc.instance.InvokeRoutedRPC(

                ZRoutedRpc.Everybody,  // who to send to
                "ChatMessage",         // RPC method name

                new object[]
                {
                    peer.m_refPos,        // position in world
                    (int)talkerType,      // chat type (Normal, Shout, Whisper)
                    "SERVER",             // sender name displayed in chat (not used ?)
                    GetPlatformId(peer),  // platform user ID (used for validation)
                    text                  // the message text
                }
            );
        }

        //
        // WARNING: This is a magic band-aid.
        //
        private string GetPlatformId(ZNetPeer peer)
        {
            var rawId = peer.m_socket.GetHostName();

            if (rawId.StartsWith("Steam_") || rawId.StartsWith("playfab/"))
            {
                return rawId;
            }

            return "Steam_" + rawId;
        }

        private bool TryParseTime(string timeStr, out int hour, out int minute)
        {
            hour = 0; minute = 0;
            if (string.IsNullOrWhiteSpace(timeStr)) return false;

            var parts = timeStr.Split(':');
            if (parts.Length != 2) return false;

            return int.TryParse(parts[0], out hour)
                && int.TryParse(parts[1], out minute)
                && hour >= 0 && hour < 24
                && minute >= 0 && minute < 60;
        }

        private void OnDestroy()
        {
            configWatcher?.Dispose();
            harmony?.UnpatchSelf();
        }
    }

}
