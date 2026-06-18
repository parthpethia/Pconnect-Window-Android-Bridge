# Pconnect

**Pconnect** is a local network (Wi‑Fi) remote control system that lets an Android phone control a Windows PC with low latency. It is designed for simple, secure, and fast communication over a LAN without requiring internet connectivity.

---

## ✨ Features

* 🔌 Local-only communication (no cloud required)
* ⚡ Low-latency WebSocket control channel
* 📡 Automatic PC discovery via UDP broadcast on the LAN
* 🔐 Secure pairing with rotating code + device token
* 🎛 Remote actions:

  * Lock PC
  * Send text input in real time
  * Launch applications (extensible)

---

## 📁 Project Structure

```
Pconnect/
├── desktop/        # Windows agent (C# / .NET)
├── mobile/         # Android app (Flutter)
├── shared/         # Protocol definitions
```

---

## 🧰 Prerequisites

### Windows PC

* .NET 8 runtime
* Open ports (Private network in Windows Firewall):

  * TCP `47821` — WebSocket control (cleartext)
  * TCP `47824` — WebSocket over TLS (Android prefers this first)
  * UDP `47822` — LAN discovery

### Android Development Machine

* Flutter SDK installed
* Android Studio (recommended)

---

## 🖥 Running the Windows Agent

```powershell
cd .\desktop\Pconnect.Agent
dotnet run
```

On first run, a pairing store is created at:

```
%AppData%\Pconnect\paired-devices.json
```

### Tray Behavior

* Runs as a system tray app
* Right-click → **Show pairing code**
* Pairing code rotates periodically for security

---

## 📦 Publishing the Windows Executable

```powershell
cd .\desktop\Pconnect.Agent
dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true
```

Output:

```
desktop/Pconnect.Agent/bin/Release/net8.0-windows/win-x64/publish/Pconnect.Agent.exe
```

### Auto-start on Login

1. Press `Win + R`
2. Enter:

   ```
   shell:startup
   ```
3. Create a shortcut to `Pconnect.Agent.exe`

---

## 📱 Running the Android App (Flutter)

Bootstrap the Flutter project, then copy the repo files:

```powershell
cd .\mobile
flutter create pconnect_mobile

# Overwrite generated files with:
# - pubspec.yaml
# - lib/main.dart

cd .\pconnect_mobile
flutter pub get
flutter run
```

---

## 📦 Building the APK

```powershell
cd .\mobile\pconnect_mobile
flutter build apk --release
```

Output APK:

```
mobile/pconnect_mobile/build/app/outputs/flutter-apk/app-release.apk
```

---

## 🚀 Usage

1. Open the Android app
2. Tap **Connect to PC**
3. Select your PC (auto-discovered) or enter IP manually
4. Enter the pairing code shown on the PC
5. Start controlling your computer:

   * Lock PC
   * Send text input

---

## 🛠 Troubleshooting

### ❌ APK Installation: "Problem parsing the package"

* Ensure you install:

  ```
  app-release.apk
  ```

  (not `.sha1`)
* Verify file size (~48MB)
* Transfer via USB (avoid compression apps)
* Enable **Install unknown apps** on Android
* Uninstall previous versions before reinstalling

**Advanced debugging:**

```powershell
adb devices -l
adb install -r .\mobile\pconnect_mobile\build\app\outputs\flutter-apk\app-release.apk
```

---

### ❌ Windows Agent Crash (UDP Port 47822 in Use)

Error: `SocketException 10048`

Check port usage:

```powershell
Get-NetUDPEndpoint -LocalPort 47822 | Select-Object LocalAddress,LocalPort,OwningProcess
Get-Process -Id (Get-NetUDPEndpoint -LocalPort 47822).OwningProcess
```

Fixes:

* Ensure only one agent instance is running
* Stop conflicting processes

---

### ❌ Android Build Fails (Paging File / Disk Full)

Error: `1455 - The paging file is too small`

Fixes:

1. Free space on `C:` drive

2. Increase or move paging file:

   * System Properties → Advanced → Performance → Virtual memory

3. (Recommended) Move Gradle cache + set JDK:

```powershell
cd .\mobile\pconnect_mobile

$env:GRADLE_USER_HOME = "d:\Projects\Pconnect\.gradle-user-home"
$env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
$env:Path = "$env:JAVA_HOME\bin;$env:Path"

flutter run
```

---

## 🔐 Security

* Uses **pairing code + per-device token**
* All communication is restricted to the local network

### Optional Enhancements

* Add TLS (`wss://`) for encrypted transport
* Use trusted certificates on Android

See:

```
shared/protocol.md
```

for full message schema details.

---

## 📌 Notes

* Designed for LAN use only
* No external servers or accounts required
* Easily extensible for additional commands

---

## 📄 License

(Add your license here)

---

## 🤝 Contributing

Pull requests and suggestions are welcome.
