import React from 'react';
import { Cpu, Smartphone, Network, ShieldCheck, FileCode2 } from 'lucide-react';
import './Architecture.css';

export default function Architecture() {
  return (
    <section id="architecture" className="section-container arch-section">
      <div className="section-header">
        <span className="status-pill" style={{ marginBottom: '16px' }}>
          Tech Stack & Protocols
        </span>
        <h2>High-Performance Tech Specs</h2>
        <p>Engineered with lightweight native technologies on both desktop and mobile platforms.</p>
      </div>

      <div className="arch-grid">
        <div className="arch-card">
          <h3>
            <Cpu size={24} color="var(--accent-light)" />
            Windows Agent (.NET 8 C#)
          </h3>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.95rem' }}>
            Lightweight, high-speed background tray service written in C# targeting .NET 8. Handles UDP broadcast beacons, multi-threaded WebSocket endpoints, Win32 API screen capture, system lock controls, and local token storage.
          </p>
          <div className="port-badge-list">
            <span className="port-badge">UDP 47822 (Discovery)</span>
            <span className="port-badge">TCP 47821 (WebSocket)</span>
            <span className="port-badge">TCP 47824 (TLS WSS)</span>
          </div>
        </div>

        <div className="arch-card">
          <h3>
            <Smartphone size={24} color="var(--accent-cyan)" />
            Android App (Flutter & Dart)
          </h3>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.95rem' }}>
            Built using Flutter for responsive 60 FPS user interface performance. Uses TOFU PIN storage, network socket listeners, background service notifications, and real-time screen buffer rendering.
          </p>
          <div className="port-badge-list">
            <span className="port-badge">Flutter 3.x / Material 3</span>
            <span className="port-badge">Local Notifications</span>
            <span className="port-badge">SharedPreferences Token Cache</span>
          </div>
        </div>
      </div>
    </section>
  );
}
