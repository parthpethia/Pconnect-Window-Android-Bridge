import React from 'react';
import { Cpu, Smartphone, ShieldCheck, Network, Lock, Wifi, Zap, ArrowRight } from 'lucide-react';
import './Architecture.css';

export default function Architecture() {
  return (
    <section id="architecture" className="section-container arch-section">
      <div className="section-header">
        <span className="status-pill" style={{ marginBottom: '16px' }}>
          <Network size={14} color="var(--accent-light)" /> System Architecture
        </span>
        <h2>High-Performance Zero-Cloud Protocol</h2>
        <p>Engineered with lightweight native technologies on both desktop (.NET C#) and mobile (Flutter Dart) platforms.</p>
      </div>

      {/* Protocol Flow Sequence */}
      <div className="protocol-sequence-card">
        <h3 className="sequence-title">
          <Zap size={20} color="var(--accent-cyan)" /> How Pconnect Connects in &lt; 3 milliseconds
        </h3>
        
        <div className="sequence-steps-grid">
          <div className="seq-step">
            <div className="step-num">01</div>
            <h4>UDP Auto-Discovery</h4>
            <span className="port-badge">UDP Port 47822</span>
            <p>Android app broadcasts a LAN discovery packet. Windows C# agent responds with desktop hostname & IP.</p>
          </div>

          <div className="seq-arrow"><ArrowRight size={20} /></div>

          <div className="seq-step">
            <div className="step-num">02</div>
            <h4>PIN Handshake</h4>
            <span className="port-badge">TOFU Security</span>
            <p>User inputs rotating 6-digit PIN. Agent generates an HMAC session token saved in encrypted local storage.</p>
          </div>

          <div className="seq-arrow"><ArrowRight size={20} /></div>

          <div className="seq-step">
            <div className="step-num">03</div>
            <h4>WebSocket Payload</h4>
            <span className="port-badge">WSS Port 47824</span>
            <p>Binary WebSockets transport touch events, keystrokes, and system lock commands with sub-3ms latency.</p>
          </div>
        </div>
      </div>

      {/* Tech Stack Cards */}
      <div className="arch-grid">
        <div className="arch-card">
          <h3>
            <Cpu size={24} color="var(--accent-light)" />
            Windows Agent (.NET 8 C#)
          </h3>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.92rem', lineHeight: '1.6' }}>
            Lightweight, high-speed background tray service written in C# targeting .NET 8. Handles UDP broadcast beacons, multi-threaded WebSocket endpoints, Win32 API screen capture, system lock controls, and local token storage.
          </p>
          <div className="port-badge-list">
            <span className="port-badge">UDP 47822 (Discovery)</span>
            <span className="port-badge">TCP 47821 (WebSocket)</span>
            <span className="port-badge">TCP 47824 (TLS WSS)</span>
            <span className="port-badge">Win32 API Lock</span>
          </div>
        </div>

        <div className="arch-card">
          <h3>
            <Smartphone size={24} color="var(--accent-cyan)" />
            Android App (Flutter & Dart)
          </h3>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.92rem', lineHeight: '1.6' }}>
            Built using Flutter for responsive 60 FPS user interface performance. Uses TOFU PIN storage, network socket listeners, background service notifications, and real-time screen buffer rendering.
          </p>
          <div className="port-badge-list">
            <span className="port-badge">Flutter 3.x / Material 3</span>
            <span className="port-badge">Local Notifications</span>
            <span className="port-badge">SharedPreferences Token Cache</span>
            <span className="port-badge">Gesture Multi-touch</span>
          </div>
        </div>
      </div>
    </section>
  );
}
