# Pconnect protocol (v1)

Transport: WebSocket over local LAN.

- Default URL: `ws://<pc-ip>:47821/ws`
- Discovery (UDP broadcast): phone broadcasts `PCONNECT_DISCOVER_V1` to `255.255.255.255:47822`, PC replies with `discoverResponse`

All messages are UTF-8 JSON objects:

- `type`: string (required)
- `v`: number protocol version (required, currently `1`)
- `requestId`: string (optional, client-generated)

Unknown message types MUST be silently ignored for forward-compatibility.

## Authentication

A client must authenticate before sending control commands.

### `hello`

Client → PC

```json
{ "v": 1, "type": "hello", "deviceId": "<uuid>", "token": "<optional>", "screenStreamModes": ["webrtc-v1", "jpeg-bin-v1", "jpeg-v1"] }
```

- `screenStreamModes` (optional): client-ordered list of preferred screen preview backends. Omitted on legacy clients; server treats that as `["jpeg-v1"]`.

PC → Client (if token valid)

```json
{
  "v": 1,
  "type": "helloAck",
  "pcName": "<name>",
  "role": "admin",
  "capabilities": ["lock", "text", "launch", "show", "mouse", "keyboard", "volume", "brightness", "shutdown", "clipboard", "fileTransfer", "recentFiles", "keyCombo", "mediaKey", "screenCapture", "notification", "appList", "customCommands", "auditLog"],
  "screenStreamModes": ["jpeg-v1"],
  "screenStream": "jpeg-v1"
}
```

- `screenStreamModes`: backends the PC can offer for screen preview (may be empty when capture is disabled, e.g. safe mode).
- `screenStream`: negotiated active backend for this session — first entry in the client's `screenStreamModes` that the PC also supports, else the PC's default. Legacy PCs omit these fields; clients assume `jpeg-v1` when `screenCapture` is advertised.
- Negotiation priority order: `webrtc-v1` → `jpeg-bin-v1` → `jpeg-v1`.

Known mode identifiers:

| Mode | Description |
|------|-------------|
| `jpeg-v1` | Low-FPS JPEG frames over WebSocket (`screenCaptureStart` / `screenFrame`). JSON/Base64 encoding. **Legacy fallback.** |
| `jpeg-bin-v1` | Low-FPS JPEG frames over binary WebSocket frames. 9-byte binary header + raw JPEG payload. No Base64 or JSON overhead. |
| `webrtc-v1` | High-performance WebRTC + H.264 stream. Uses WebRTC video tracks and a binary data channel for input events. |

- `role`: `"admin"` | `"media_only"` | `"readonly"` — the device's permission role

PC → Client (if not paired)

```json
{ "v": 1, "type": "authRequired", "pairing": { "method": "code" } }
```

### `pair`

Client → PC

```json
{
  "v": 1,
  "type": "pair",
  "deviceId": "<uuid>",
  "code": "123456",
  "deviceName": "Phone"
}
```

PC → Client

```json
{ "v": 1, "type": "paired", "deviceId": "<uuid>", "token": "<random-token>", "role": "admin" }
```

- New devices are assigned `"admin"` role by default.

## Commands (require auth)

### Lock PC

Client → PC

```json
{ "v": 1, "type": "lock" }
```

PC → Client

```json
{ "v": 1, "type": "ok" }
```

### Text input (low latency)

Client → PC

```json
{ "v": 1, "type": "input", "backspaces": 2, "text": "hello" }
```

- The PC will send `backspaces` times the Backspace key, then type `text` as Unicode.

### Replace All Text (large/destructive edits)

Client → PC

```json
{ "v": 1, "type": "replaceAllText", "text": "hello world" }
```

- Replaces the entire focused field's content with `text`. The PC will select all text via Ctrl+A, then paste the new text using clipboard-paste.
- **Role**: requires `admin`.

### Keyboard (virtual-key events)

Use this for modifier keys (Ctrl/Shift/Alt/Win) and key combos.

Client → PC

```json
{ "v": 1, "type": "key", "vk": 65, "action": "press" }
```

```json
{ "v": 1, "type": "key", "vk": 17, "action": "down" }
```

```json
{ "v": 1, "type": "key", "vk": 17, "action": "up" }
```

- `vk`: Win32 virtual-key code (integer)
- `action`: `press` | `down` | `up`
- Optional: `extended`: boolean (for extended keys like arrows)

### Key Combo (named-key shortcut)

Client → PC

```json
{ "v": 1, "type": "keyCombo", "keys": ["ctrl", "shift", "esc"] }
```

- `keys`: array of named keys. Supported names:
  `ctrl`, `shift`, `alt`, `win`, `enter`, `tab`, `esc`, `space`, `backspace`, `delete`,
  `up`, `down`, `left`, `right`, `home`, `end`, `pageup`, `pagedown`,
  `f1`–`f12`, `a`–`z`, `0`–`9`,
  and any single character.
- The PC presses all modifier keys down, presses the last key, then releases all.
- **Role**: requires `admin`.

PC → Client

```json
{ "v": 1, "type": "ok" }
```

### Media Keys

Client → PC

```json
{ "v": 1, "type": "mediaKey", "key": "play_pause" }
```

- `key`: `"play_pause"` | `"next"` | `"prev"` | `"stop"` | `"mute"` | `"vol_up"` | `"vol_down"`
- **Role**: requires `admin` or `media_only`.

PC → Client

```json
{ "v": 1, "type": "ok" }
```

### Set system volume

Client → PC

```json
{ "v": 1, "type": "setVolume", "level": 35 }
```

- `level`: integer `0..100`
- **Role**: requires `admin` or `media_only`.

PC → Client

```json
{ "v": 1, "type": "ok" }
```

### Set screen brightness

Client → PC

```json
{ "v": 1, "type": "setBrightness", "level": 60 }
```

- `level`: integer `0..100`

PC → Client

```json
{ "v": 1, "type": "ok" }
```

### Launch an application

Client → PC

```json
{ "v": 1, "type": "launch", "command": "notepad", "args": ["C:/temp/a.txt"] }
```

### Launch app (by path from app list)

Client → PC

```json
{ "v": 1, "type": "launchApp", "exePath": "C:\\Program Files\\..." }
```

PC → Client

```json
{ "v": 1, "type": "ok" }
```

### Show agent UI (bring to front)

Client → PC

```json
{ "v": 1, "type": "show" }
```

### Shut down PC

Client → PC

```json
{ "v": 1, "type": "shutdown", "password": "<configured-pin>" }
```

- `password`: required (configured on PC via `PCONNECT_SHUTDOWN_PIN` environment variable)

PC → Client

```json
{ "v": 1, "type": "ok" }
```

### Clipboard sync

#### Set clipboard (from phone to PC)

Client → PC

```json
{
  "v": 1,
  "type": "clipboardSet",
  "data": "<base64-encoded-utf8>",
  "format": "text/plain"
}
```

- `data`: Base64-encoded text content
- `format`: MIME type (currently `text/plain`)

PC → Client

```json
{ "v": 1, "type": "ok" }
```

#### Clipboard update (from PC to phone, unsolicited)

PC → Client (pushed when system clipboard changes on PC)

```json
{
  "v": 1,
  "type": "clipboardUpdate",
  "data": "<base64-encoded-utf8>",
  "format": "text/plain",
  "source": "system"
}
```

- Phone receives this when user copies on PC
- `source`: always `"system"` (for future extension to other sources)

### File Transfer

#### Initiate transfer

Client → PC

```json
{
  "v": 1,
  "type": "fileTransferStart",
  "id": "<uuid>",
  "filename": "document.pdf",
  "size": 1048576,
  "direction": "upload"
}
```

- `id`: Unique transfer ID
- `filename`: Desired filename
- `size`: Total file size in bytes
- `direction`: `"upload"` (phone→PC) or `"download"` (PC→phone)

PC → Client (ack)

```json
{ "v": 1, "type": "fileTransferAck", "id": "<uuid>", "ready": true }
```

#### Transfer chunk

Client → PC

```json
{
  "v": 1,
  "type": "fileTransferChunk",
  "id": "<uuid>",
  "chunkIndex": 0,
  "totalChunks": 20,
  "data": "<base64-chunk>",
  "size": 52428
}
```

- `data`: Base64-encoded chunk (50KB recommended)
- `chunkIndex`: 0-indexed chunk number
- `totalChunks`: Total number of chunks

PC → Client (progress)

```json
{
  "v": 1,
  "type": "fileTransferProgress",
  "id": "<uuid>",
  "chunkIndex": 0,
  "received": 52428,
  "total": 1048576
}
```

#### Complete transfer

Client → PC

```json
{ "v": 1, "type": "fileTransferComplete", "id": "<uuid>" }
```

PC → Client

```json
{ "v": 1, "type": "fileTransferComplete", "id": "<uuid>", "status": "success" }
```

#### Abort transfer

Client → PC

```json
{ "v": 1, "type": "fileTransferAbort", "id": "<uuid>" }
```

PC → Client

```json
{ "v": 1, "type": "ok" }
```

### Recent Files

Client → PC

```json
{ "v": 1, "type": "listRecentFiles", "limit": 20 }
```

PC → Client

```json
{
  "v": 1,
  "type": "recentFilesList",
  "files": [
    {
      "path": "C:\\Users\\User\\Documents\\report.docx",
      "name": "report.docx",
      "modified": 1712700000000,
      "size": 102400
    }
  ],
  "status": "ok"
}
```

- `files`: Array of {path, name, modified (timestamp ms), size}

### Mouse / Trackpad control

These messages are designed to be sent frequently (especially `mouseMove`).

#### Move mouse (relative)

Client → PC

```json
{ "v": 1, "type": "mouseMove", "dx": 12, "dy": -4 }
```

- `dx`, `dy` are relative deltas (pixels).

#### Scroll (vertical mouse wheel)

Client → PC

```json
{ "v": 1, "type": "mouseScroll", "dy": -120 }
```

- `dy` is the wheel delta (same convention as Win32 wheel delta; a common notch is `120`).

#### Mouse button

Client → PC

```json
{ "v": 1, "type": "mouseButton", "button": "left", "action": "click" }
```

```json
{ "v": 1, "type": "mouseButton", "button": "left", "action": "down" }
```

```json
{ "v": 1, "type": "mouseButton", "button": "left", "action": "up" }
```

- `button`: `left` | `right` | `middle`
- `action`: `click` | `down` | `up`

### Screen Capture

If negotiated mode is `webrtc-v1`, the client does not send `screenCaptureStart`. Instead, signaling and media flow as described below. If negotiation or connection fails, it falls back to `jpeg-bin-v1` (preferred) or `jpeg-v1`.

#### WebRTC Signaling Messages

Signaling messages are sent over the existing WebSocket connection.

Client → PC (Offer):
```json
{ "v": 1, "type": "webrtcOffer", "sdp": "<SDP string>" }
```

PC → Client (Answer):
```json
{ "v": 1, "type": "webrtcAnswer", "sdp": "<SDP string>" }
```

Both directions (ICE Candidate):
```json
{ "v": 1, "type": "webrtcIce", "candidate": "<candidate>", "sdpMid": "0", "sdpMLineIndex": 0 }
```

PC → Client (Ready - sent after ICE connected):
```json
{ "v": 1, "type": "webrtcReady" }
```

PC → Client (Fallback - sent if WebRTC connection fails or times out):
```json
{ "v": 1, "type": "webrtcFallback", "mode": "jpeg-bin-v1" }
```

The `mode` field indicates the fallback backend (`jpeg-bin-v1` if the client supports it, otherwise `jpeg-v1`).

#### Data Channel Input Protocol

Touch, gesture, and keyboard events are sent over the WebRTC data channel (labeled `"input"`, unordered, unreliable) as a 10-byte binary packet:

```
[0]     event type   (0x01=move, 0x02=button_down, 0x03=button_up, 0x04=key)
[1-4]   x            (int32, big-endian)
[5-8]   y            (int32, big-endian)
[9]     button/keycode
```

- For mouse move (`0x01`): `x` and `y` are the relative deltas.
- For mouse button down/up (`0x02`/`0x03`): `button` is `0` (left), `1` (right), or `2` (middle).
- For key (`0x04`): `x` contains the virtual key code (lower 16 bits), `y` contains the action (`0`=press, `1`=down, `2`=up), and byte `[9]` is the extended key flag (`0` or `1`).

#### Legacy/Fallback Screen Capture (low-fps preview)

Production uses negotiated mode `jpeg-v1` or `jpeg-bin-v1` (see handshake `screenStream`).

##### Binary Screen Frame (`jpeg-bin-v1`)

When `jpeg-bin-v1` is negotiated, screen frames are sent as **binary WebSocket messages** with a 9-byte header followed by raw JPEG bytes:

```
[0]     message type    (1 byte)  — always 0x01 for screen frame
[1-4]   frame width     (4 bytes) — uint32, big-endian
[5-8]   frame height    (4 bytes) — uint32, big-endian
[9+]    raw JPEG bytes  (variable)
```

Total header overhead: 9 bytes. No Base64 encoding, no JSON wrapping. The JPEG payload from the capture pipeline is delivered as-is.

##### Enable/disable capture

Client → PC

```json
{ "v": 1, "type": "screenCaptureStart", "intervalMs": 2000 }
```

```json
{ "v": 1, "type": "screenCaptureStop" }
```

##### Screen frame (pushed by PC while capture active)

PC → Client

```json
{
  "v": 1,
  "type": "screenFrame",
  "data": "<base64-jpeg>",
  "width": 480,
  "height": 270
}
```

### App List

Client → PC

```json
{ "v": 1, "type": "getAppList" }
```

PC → Client

```json
{
  "v": 1,
  "type": "appList",
  "apps": [
    {
      "name": "Notepad",
      "iconBase64": "<base64-png>",
      "exePath": "C:\\Windows\\notepad.exe"
    }
  ]
}
```

### Custom Commands

Client → PC

```json
{ "v": 1, "type": "getCommands" }
```

PC → Client

```json
{
  "v": 1,
  "type": "commandList",
  "commands": [
    { "label": "Start OBS", "command": "cmd /c start obs64.exe" }
  ]
}
```

Client → PC

```json
{ "v": 1, "type": "runCommand", "index": 0 }
```

PC → Client

```json
{ "v": 1, "type": "ok" }
```

### Notification Mirror

PC → Client (pushed when a toast notification appears on PC)

```json
{
  "v": 1,
  "type": "notification",
  "title": "New email",
  "body": "You have a new email from ...",
  "appName": "Microsoft Outlook"
}
```

### Settings Sync

Client → PC

```json
{ "v": 1, "type": "settingsSync", "autoLockOnDisconnect": true }
```

PC → Client

```json
{ "v": 1, "type": "ok" }
```

### Audit Log

Client → PC

```json
{ "v": 1, "type": "getLogs", "date": "2026-05-09" }
```

PC → Client

```json
{
  "v": 1,
  "type": "logEntries",
  "entries": [
    { "time": "2026-05-09T14:30:00+05:30", "device": "Android Phone", "action": "lock" }
  ]
}
```

## Error

PC → Client

```json
{ "v": 1, "type": "error", "message": "..." }
```

## Discovery (UDP broadcast)

Phone → LAN broadcast (`255.255.255.255:47822`)

```text
PCONNECT_DISCOVER_V1
```

PC → Phone (unicast reply)

```json
{ "v": 1, "type": "discoverResponse", "pcName": "<name>", "wsPort": 47821 }
```
