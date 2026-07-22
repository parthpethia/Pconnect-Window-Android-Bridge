import React, { useEffect } from 'react';
import { ShieldCheck, HardDrive, Wifi, Lock, EyeOff, UserCheck, HelpCircle, CheckCircle2 } from 'lucide-react';
import '../components/Privacy.css';

export default function Privacy() {
  useEffect(() => {
    window.scrollTo(0, 0);
  }, []);

  return (
    <div className="privacy-container">
      <div className="privacy-header">
        <span className="status-pill" style={{ marginBottom: '16px' }}>
          <ShieldCheck size={16} /> Privacy Policy & Data Commitment
        </span>
        <h1>Privacy Policy</h1>
        <div className="privacy-meta">
          <span>Effective Date: July 22, 2026</span>
          <span>•</span>
          <span>Version 1.0</span>
          <span>•</span>
          <span style={{ color: 'var(--accent-emerald)', fontWeight: 700 }}>100% Local LAN Only</span>
        </div>
      </div>

      <div className="privacy-content">
        <div className="highlight-box">
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '8px', fontWeight: 700, color: 'var(--accent-emerald)', fontSize: '1.1rem' }}>
            <CheckCircle2 size={20} />
            <span>Core Guarantee: Zero Cloud & Zero Telemetry</span>
          </div>
          <p style={{ margin: 0, fontSize: '0.95rem' }}>
            Pconnect is built from the ground up as a <strong>local-first software system</strong>. We do not operate any external cloud servers, tracking APIs, analytics services, or user advertising networks. All communication remains strictly contained within your personal local network (Wi-Fi/LAN).
          </p>
        </div>

        <section className="privacy-section">
          <h2>
            <HardDrive size={22} />
            1. Information We Storage Locally
          </h2>
          <p>
            Pconnect does not transmit any user information off your devices. Any configuration or authentication state required for seamless operation is stored locally on your own hardware:
          </p>
          <ul className="privacy-list">
            <li>
              <CheckCircle2 size={18} color="var(--accent-light)" />
              <div>
                <strong>Pairing Tokens & Device IDs:</strong> When you pair an Android device with your PC, a random UUID token and device name are stored locally in <code>%AppData%\Pconnect\paired-devices.json</code> on Windows and in <code>SharedPreferences</code> on Android.
              </div>
            </li>
            <li>
              <CheckCircle2 size={18} color="var(--accent-light)" />
              <div>
                <strong>User Preferences:</strong> Dark/light UI mode settings and recent connection target IPs are stored exclusively in local app storage.
              </div>
            </li>
          </ul>
        </section>

        <section className="privacy-section">
          <h2>
            <Wifi size={22} />
            2. Local Network Usage & Communication
          </h2>
          <p>
            Pconnect utilizes standard local network protocols for peer-to-peer connection between your phone and PC:
          </p>
          <ul className="privacy-list">
            <li>
              <strong style={{ minWidth: '130px', color: 'var(--accent-cyan)' }}>UDP Port 47822:</strong> Used exclusively for LAN device discovery broadcasts so your phone can automatically locate your PC on your home router.
            </li>
            <li>
              <strong style={{ minWidth: '130px', color: 'var(--accent-cyan)' }}>TCP Port 47821:</strong> Low-latency WebSocket channel for control actions (locking PC, sending text input, touch commands).
            </li>
            <li>
              <strong style={{ minWidth: '130px', color: 'var(--accent-cyan)' }}>TCP Port 47824:</strong> Optional TLS-encrypted WebSocket channel for secure payload transfer over LAN.
            </li>
          </ul>
        </section>

        <section className="privacy-section">
          <h2>
            <Lock size={22} />
            3. Screen Share & Remote Input Handling
          </h2>
          <p>
            When you activate remote screen capture or file transfer features:
          </p>
          <ul className="privacy-list">
            <li>
              <CheckCircle2 size={18} color="var(--accent-emerald)" />
              <div>
                Screen frames are captured live in system memory, compressed into JPEG/binary buffers, and transmitted directly to your paired phone screen over WebSockets.
              </div>
            </li>
            <li>
              <CheckCircle2 size={18} color="var(--accent-emerald)" />
              <div>
                No video recordings or screen frames are ever uploaded, recorded to disk, or sent to third-party endpoints.
              </div>
            </li>
          </ul>
        </section>

        <section className="privacy-section">
          <h2>
            <UserCheck size={22} />
            4. User Rights & Control
          </h2>
          <p>
            You maintain 100% control over your data and devices at all times:
          </p>
          <ul className="privacy-list">
            <li>
              <strong>Revoking Authorization:</strong> You can right-click the Pconnect Windows tray icon or clear pairing tokens in the settings to disconnect paired devices at any moment.
            </li>
            <li>
              <strong>Deleting App Data:</strong> Uninstalling the mobile app or clearing app storage permanently removes all stored tokens from your Android device.
            </li>
          </ul>
        </section>

        <section className="privacy-section">
          <h2>
            <HelpCircle size={22} />
            5. Open Source Transparency
          </h2>
          <p>
            Pconnect is committed to total transparency. You are encouraged to inspect our complete source code on GitHub to verify our local network implementation and privacy guarantees.
          </p>
        </section>
      </div>
    </div>
  );
}
