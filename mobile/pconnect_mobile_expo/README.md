# Pconnect Mobile (Expo React Native) 📱

This is the React Native / Expo version of the Pconnect mobile application. It has been set up using the modern Expo template with Expo Router and TypeScript.

## 🚀 Getting Started

Since the `D:` drive has ample space but the `C:` drive is running low, it's recommended to run your commands using a custom cache and temporary directory configuration on the `D:` drive.

### 1. Start the Development Server

To start the Expo bundler:

```powershell
# Set temporary and cache directories on D: to prevent C: drive out-of-space errors
$env:TMP="d:\Projects\Pconnect\.tmp"
$env:TEMP="d:\Projects\Pconnect\.tmp"
npm run start -- --cache d:\Projects\Pconnect\.npm-cache
```

This will spin up the Metro Bundler and print a QR code in the terminal.

### 2. Run on Device or Emulator

* **Expo Go (Physical Device)**: Install the **Expo Go** app from the Google Play Store (Android) or App Store (iOS). Scan the QR code printed by Metro with your device camera or the Expo Go app to launch the development build.
* **Android Emulator**:
  ```powershell
  $env:TMP="d:\Projects\Pconnect\.tmp"
  $env:TEMP="d:\Projects\Pconnect\.tmp"
  npm run android
  ```
* **Web**:
  ```powershell
  npm run web
  ```

---

## 🛠 Features of Expo Debugging

Expo provides an exceptionally rich development and debugging experience:
1. **Fast Refresh**: Changes in your react components are immediately reflected in the app without losing state.
2. **Chrome DevTools Integration**: Debug JavaScript/TypeScript directly inside Google Chrome or Microsoft Edge.
3. **Expo Orbit / Expo Go**: Instantly run on physical devices without compiling native binaries.
4. **Log Mirroring**: App console logs and stack traces are mirrored directly in your terminal.

---

## 📁 File Structure

- [src/app](file:///d:/Projects/Pconnect/mobile/pconnect_mobile_expo/src/app): Navigation structure and pages (file-based routing).
  - [_layout.tsx](file:///d:/Projects/Pconnect/mobile/pconnect_mobile_expo/src/app/_layout.tsx): Root layout setting up providers and themes.
  - [index.tsx](file:///d:/Projects/Pconnect/mobile/pconnect_mobile_expo/src/app/index.tsx): The home/welcome screen.
- [src/components](file:///d:/Projects/Pconnect/mobile/pconnect_mobile_expo/src/components): Reusable React Native components.
- [src/hooks](file:///d:/Projects/Pconnect/mobile/pconnect_mobile_expo/src/hooks): Custom React hooks.
