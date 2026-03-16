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
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class ScheduledMessagesPlugin : BaseUnityPlugin
    {
        public const string PluginGUID    = "com.yourname.scheduledmessages";
        public const string PluginName    = "ScheduledMessages";
        public const string PluginVersion = "1.4.0";

        internal static ManualLogSource Log;
        private Harmony harmony;

        private double timezoneOffsetHours = 0;
        private string welcomeMessage = "";
        private float welcomeDelay = 30f;
        private List<ScheduledMessage> messages = new List<ScheduledMessage>();

        private readonly HashSet<string> sentThisMinute = new HashSet<string>();
        private int lastCheckedMinute = -1;

        private readonly HashSet<long> knownPeers = new HashSet<long>();

        private string configPath;
        private FileSystemWatcher configWatcher;

        private void Awake()
        {
            Log = Logger;
            harmony = new Harmony(PluginGUID);
            harmony.PatchAll();

            configPath = Path.Combine(Paths.ConfigPath, "scheduledmessages.cfg");

            LoadConfig();
            StartConfigWatcher();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded — {messages.Count} scheduled message(s) found.");
        }

        private void LoadConfig()
        {
            if (!File.Exists(configPath))
            {
                Log.LogError($"Config not found at {configPath}. No welcome or scheduled messages will be sent.");
                return;
            }

            var newMessages = new List<ScheduledMessage>();
            double newTimezone = 0;
            string newWelcome = "";

            foreach (string raw in File.ReadAllLines(configPath))
            {
                string line = raw.Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                if (line.StartsWith("timezone="))
                {
                    double.TryParse(line.Substring("timezone=".Length).Trim(), out newTimezone);
                }
                else if (line.StartsWith("welcome="))
                {
                    newWelcome = line.Substring("welcome=".Length).Trim();
                }
                else if (line.StartsWith("welcome-delay="))
                {
                    float.TryParse(line.Substring("welcome-delay=".Length).Trim(), out welcomeDelay);
                }
                else
                {
                    int space = line.IndexOf(' ');
                    if (space < 0) continue;

                    string time = line.Substring(0, space).Trim();
                    string text = line.Substring(space + 1).Trim();

                    if (TryParseTime(time, out int hour, out int minute) && !string.IsNullOrEmpty(text))
                    {
                        newMessages.Add(new ScheduledMessage { Hour = hour, Minute = minute, Time = time, Message = text });
                    }
                }
            }

            timezoneOffsetHours = newTimezone;
            welcomeMessage = newWelcome;
            messages = newMessages;
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
            System.Threading.Thread.Sleep(200);
            Log.LogInfo("Config file changed, reloading...");
            LoadConfig();
            Log.LogInfo($"Config reloaded — {messages.Count} scheduled message(s) found.");
            if (welcomeMessage == "")
            {
                Log.LogInfo("No welcome message was configured.");
            }
        }

        private void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            if (welcomeMessage != "")
            {
                CheckForNewPeers();
            }

            if (messages.Count == 0) return;

            DateTime now = DateTime.UtcNow.AddHours(timezoneOffsetHours);
            int currentMinute = now.Hour * 60 + now.Minute;

            if (currentMinute != lastCheckedMinute)
            {
                sentThisMinute.Clear();
                lastCheckedMinute = currentMinute;
            }

            foreach (var entry in messages)
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
            if (string.IsNullOrEmpty(welcomeMessage)) return;

            var peers = ZNet.instance.GetPeers();

            foreach (var peer in peers)
            {
                if (peer.m_uid == 0) continue;  // ignore ghost peers

                if (!knownPeers.Contains(peer.m_uid))
                {
                    knownPeers.Add(peer.m_uid);
                    Log.LogInfo($"New peer connected: {peer.m_playerName} ({peer.m_uid}), sending welcome message.");
                    StartCoroutine(SendDelayedWelcome(peer));
                }
            }

            // Clean up peers that have disconnected
            var currentUids = new HashSet<long>(peers.Select(p => p.m_uid));
            knownPeers.RemoveWhere(uid => !currentUids.Contains(uid));
        }

        private IEnumerator SendDelayedWelcome(ZNetPeer peer)
        {
            yield return new WaitForSeconds(welcomeDelay);

            if (peer == null)
            {
                Log.LogInfo($"[Welcome] no peer found to send welcome to.");
                yield break;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(

                ZRoutedRpc.Everybody,  // who to send to
                "ChatMessage",         // RPC method name

                new object[]
                {
                    peer.m_refPos,               // position in world
                    (int)Talker.Type.Normal,     // chat type (Normal, Shout, Whisper)
                    "Server",                    // sender name displayed in chat
                    peer.m_socket.GetHostName(), // platform user ID (used for validation)
                    welcomeMessage               // the message text
                }
            );

            Log.LogInfo($"[Welcome] Sent to {peer.m_playerName}: {welcomeMessage}");
        }

        private void Broadcast(string text)
        {
            var peers = ZNet.instance.GetPeers();

            if (!peers.Any())
            {
                Log.LogInfo($"[Broadcast] No peers connected, skipping: {text}");
                return;
            }

            foreach (var peer in peers)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(

                    ZRoutedRpc.Everybody,  // who to send to
                    "ChatMessage",         // RPC method name

                    new object[]
                    {
                        peer.m_refPos,               // position in world
                        (int)Talker.Type.Shout,      // chat type (Normal, Shout, Whisper)
                        "Server",                    // sender name displayed in chat
                        peer.m_socket.GetHostName(), // platform user ID (used for validation)
                        text                         // the message text
                    }
                );
            }

            Log.LogInfo($"[Broadcast] {text}");
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

    public class ScheduledMessage
    {
        public int Hour;
        public int Minute;
        public string Time;
        public string Message;
    }
}
