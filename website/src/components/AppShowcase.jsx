import React, { useState } from 'react';
import { Smartphone, Monitor, ShieldCheck, Wifi, Lock, Zap, CheckCircle2, Terminal, ArrowRight } from 'lucide-react';
import './AppShowcase.css';

export default function AppShowcase() {
  const [activeTab, setActiveTab] = useState('mobile'); // 'mobile' or 'desktop'

  return (
    <section id="showcase" className="section-container showcase-section">
      <div className="section-header">
        <span className="status-pill" style={{ marginBottom: '16px' }}>
          <Smartphone size={14} color="var(--accent-cyan)" /> Native Product Showcase
        </span>
        <h2>Explore Native Mobile & Desktop UIs</h2>
        <p>Built with Flutter 3 for 60 FPS mobile fluid performance and .NET 8 for a lightweight Windows tray agent.</p>

        {/* Category Switcher */}
        <div className="showcase-tab-switcher">
          <button 
            className={`showcase-tab ${activeTab === 'mobile' ? 'active' : ''}`}
            onClick={() => setActiveTab('mobile')}
          >
            <Smartphone size={18} /> Android App (Flutter)
          </button>
          <button 
            className={`showcase-tab ${activeTab === 'desktop' ? 'active' : ''}`}
            onClick={() => setActiveTab('desktop')}
          >
            <Monitor size={18} /> Windows Agent (.NET C#)
          </button>
        </div>
      </div>

      {activeTab === 'mobile' ? (
        <div className="showcase-grid">
          {/* Mobile Screen 1 */}
          <div className="showcase-card">
            <div className="screen-mockup-frame mobile-frame">
              <div className="mock-screen-header">
                <span className="time-text">12:35</span>
                <span className="wifi-icon"><Wifi size={12} color="#10b981" /></span>
              </div>
              <div className="mock-screen-body">
                <div className="mock-app-header">
                  <img src="/logo-pconnect.png" alt="Logo" className="mock-logo" />
                  <span className="mock-app-title">Pconnect</span>
                  <span className="mock-badge-online">Online</span>
                </div>

                <div className="mock-device-card">
                  <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                    <Monitor size={20} color="var(--accent-light)" />
                    <div>
                      <div style={{ fontWeight: 700, fontSize: '0.85rem' }}>PARTH-DESKTOP</div>
                      <div style={{ fontSize: '0.7rem', color: 'var(--text-muted)' }}>192.168.1.105:47821</div>
                    </div>
                  </div>
                  <CheckCircle2 size={16} color="#10b981" />
                </div>

                <div className="mock-pin-box">
                  <div style={{ fontSize: '0.7rem', color: 'var(--text-muted)' }}>ENTER PAIRING CODE</div>
                  <div className="pin-digits">7 4 9 - 3 8 2</div>
                  <div className="pin-hint">Code expires in 45s</div>
                </div>

                <div className="mock-btn-primary">Connect to PC</div>
              </div>
            </div>
            <div className="card-info">
              <h3>1. Auto-Discovery & TOFU Pairing</h3>
              <p>UDP broadcast auto-detects Windows PCs on your local Wi-Fi. Enter the rotating 6-digit PIN once to establish encrypted trust.</p>
              <div className="tech-tags">
                <span>UDP 47822</span>
                <span>TOFU Verification</span>
                <span>Zero Cloud</span>
              </div>
            </div>
          </div>

          {/* Mobile Screen 2 */}
          <div className="showcase-card">
            <div className="screen-mockup-frame mobile-frame">
              <div className="mock-screen-header">
                <span className="time-text">12:36</span>
                <span className="wifi-icon"><Wifi size={12} color="#10b981" /></span>
              </div>
              <div className="mock-screen-body">
                <div className="mock-app-header">
                  <span className="mock-app-title">Remote Trackpad</span>
                  <span className="mock-ping">2.1 ms</span>
                </div>

                <div className="mock-trackpad-area">
                  <div className="gesture-indicator">
                    <span className="ripple"></span>
                    <span style={{ fontSize: '0.75rem', color: 'var(--text-secondary)' }}>Multi-touch Trackpad</span>
                  </div>
                </div>

                <div className="mock-click-pads">
                  <div className="click-pad">Left Click</div>
                  <div className="click-pad">Right Click</div>
                </div>
              </div>
            </div>
            <div className="card-info">
              <h3>2. Precision Remote Touchpad</h3>
              <p>Low-latency gesture surface supporting 1-finger cursor movement, 2-finger scrolling, and instant left/right clicks.</p>
              <div className="tech-tags">
                <span>Binary WS Payload</span>
                <span>Sub-3ms Latency</span>
                <span>Smooth Inertia</span>
              </div>
            </div>
          </div>

          {/* Mobile Screen 3 */}
          <div className="showcase-card">
            <div className="screen-mockup-frame mobile-frame">
              <div className="mock-screen-header">
                <span className="time-text">12:37</span>
                <span className="wifi-icon"><Wifi size={12} color="#10b981" /></span>
              </div>
              <div className="mock-screen-body">
                <div className="mock-app-header">
                  <span className="mock-app-title">Quick Action Hub</span>
                </div>

                <div className="mock-grid-actions">
                  <div className="mock-tile active">
                    <Lock size={18} color="#ff7675" />
                    <span>Lock PC</span>
                  </div>
                  <div className="mock-tile">
                    <Zap size={18} color="#00cec9" />
                    <span>Send Text</span>
                  </div>
                  <div className="mock-tile">
                    <Monitor size={18} color="#a29bfe" />
                    <span>Screen Mirror</span>
                  </div>
                  <div className="mock-tile">
                    <ShieldCheck size={18} color="#10b981" />
                    <span>Mute Audio</span>
                  </div>
                </div>
              </div>
            </div>
            <div className="card-info">
              <h3>3. One-Tap Remote Actions</h3>
              <p>Instantly lock your workstation when stepping away, mute PC volume, or stream text input straight from your phone keyboard.</p>
              <div className="tech-tags">
                <span>Win32 API Bridge</span>
                <span>System Lock</span>
                <span>Key Injection</span>
              </div>
            </div>
          </div>
        </div>
      ) : (
        <div className="showcase-grid desktop-grid">
          {/* Desktop Screen 1 */}
          <div className="showcase-card desktop-card">
            <div className="screen-mockup-frame desktop-frame">
              <div className="mock-win-titlebar">
                <div className="win-controls">
                  <span className="dot red"></span>
                  <span className="dot yellow"></span>
                  <span className="dot green"></span>
                </div>
                <span>Pconnect Agent v1.0.0 (Windows System Tray)</span>
              </div>
              <div className="mock-win-body">
                <div className="win-agent-card">
                  <div className="agent-status-header">
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                      <Monitor size={22} color="var(--accent-light)" />
                      <div>
                        <div style={{ fontWeight: 700 }}>DESKTOP-PARTH (C# Agent)</div>
                        <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>LAN IP: 192.168.1.105</div>
                      </div>
                    </div>
                    <span className="badge-active">RUNNING</span>
                  </div>

                  <div className="win-stats-row">
                    <div className="win-stat">
                      <span className="stat-num">47821</span>
                      <span className="stat-label">WebSocket Port</span>
                    </div>
                    <div className="win-stat">
                      <span className="stat-num">47822</span>
                      <span className="stat-label">UDP Discovery</span>
                    </div>
                    <div className="win-stat">
                      <span className="stat-num">15 MB</span>
                      <span className="stat-label">RAM Usage</span>
                    </div>
                  </div>

                  <div className="win-code-banner">
                    <span style={{ fontSize: '0.8rem', color: 'var(--text-muted)' }}>CURRENT PAIRING PIN:</span>
                    <span className="banner-pin">749 - 382</span>
                  </div>
                </div>
              </div>
            </div>
            <div className="card-info">
              <h3>Windows Tray Agent Dashboard</h3>
              <p>Runs efficiently in the Windows system tray. Uses native C# .NET 8 background workers with zero impact on gaming or workstation performance.</p>
              <div className="tech-tags">
                <span>.NET 8 Runtime</span>
                <span>Win32 Tray Service</span>
                <span>Memory Footprint &lt; 20MB</span>
              </div>
            </div>
          </div>

          {/* Desktop Screen 2 */}
          <div className="showcase-card desktop-card">
            <div className="screen-mockup-frame desktop-frame">
              <div className="mock-win-titlebar">
                <div className="win-controls">
                  <span className="dot red"></span>
                  <span className="dot yellow"></span>
                  <span className="dot green"></span>
                </div>
                <span>Paired Devices & Security Log</span>
              </div>
              <div className="mock-win-body">
                <div className="security-log-box">
                  <div className="device-row">
                    <Smartphone size={18} color="var(--accent-cyan)" />
                    <div style={{ flex: 1 }}>
                      <div style={{ fontWeight: 700, fontSize: '0.85rem' }}>Samsung Galaxy S23 (Mobile)</div>
                      <div style={{ fontSize: '0.7rem', color: 'var(--text-muted)' }}>Token: 8a9f...e201 • TLS 1.3</div>
                    </div>
                    <span className="badge-connected">Active Session</span>
                  </div>

                  <div className="security-event-list">
                    <div className="sec-event">
                      <span className="time">12:35:10</span>
                      <span className="msg">UDP Discovery Packet Received from 192.168.1.110</span>
                    </div>
                    <div className="sec-event">
                      <span className="time">12:35:12</span>
                      <span className="msg">WebSocket TLS Handshake OK (Port 47824)</span>
                    </div>
                    <div className="sec-event">
                      <span className="time">12:35:15</span>
                      <span className="msg">PIN Code 749-382 Verified • Access Granted</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
            <div className="card-info">
              <h3>Secure Pairing Store & Active Sessions</h3>
              <p>Store encrypted pairing tokens locally in <code>%AppData%/Pconnect/paired-devices.json</code>. Reject untrusted devices automatically.</p>
              <div className="tech-tags">
                <span>AES Token Cryptography</span>
                <span>TLS 1.3 Transport</span>
                <span>Session Store</span>
              </div>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
