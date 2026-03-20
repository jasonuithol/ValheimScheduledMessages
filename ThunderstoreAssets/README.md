# ScheduledMessages

A simple server-side BepInEx plugin for Valheim that broadcasts scheduled messages to all players, and sends a welcome message to each player when they connect.

No client-side installation required.

---

## Features

- Broadcast messages to all players at specific times of day
- Send a welcome message to each player when they join
- Configurable welcome delay (to ensure the player is fully loaded in before the message appears)
- Live config reloading — edit the config file while the server is running and changes take effect immediately, no restart required

---

## Manual Installation (if for some reason the package manager doesn't work)

1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) on your Valheim server
2. Copy `ScheduledMessages.dll` into `BepInEx/plugins/`
3. Copy `scheduledmessages.cfg` into `BepInEx/config/`
4. Start the server

---

## Configuration

The config file lives at `BepInEx/config/scheduledmessages.cfg`.

```
# ScheduledMessages config

# timezone: UTC offset in hours
#   e.g.  10 = AEST (Queensland)
#   e.g.  11 = AEDT (NSW/VIC during daylight saving)
#   e.g.  -5 = EST (US Eastern)
timezone=10

# welcome: message sent to each player after they connect
# leave blank or remove this line to disable
welcome=Welcome! Server restarts at 12am and 5am (AEST).

# welcome-delay: seconds to wait after connection before sending the welcome message
# a delay is recommended to ensure the player is fully loaded in
welcome-delay=30

# Scheduled messages: HH:mm <message text>  (24 hour time, one per line)
11:30 Server restart in 30 minutes.
11:40 Server restart in 20 minutes.
11:50 Server restart in 10 minutes.
11:59 Server restart in 1 minute. Find somewhere safe!
```

### Notes

- Times are in 24-hour format
- The timezone offset is applied to UTC, so set it to match your server's local time
- Lines starting with `#` are comments and are ignored
- The config file is watched for changes — just save the file and the new config will be picked up automatically. The log will confirm how many messages were loaded.

---

## Compatibility

- Valheim `0.221.12` (network version 36)
- BepInEx `5.4.23.x`
- Server-side only — players do not need to install this mod
