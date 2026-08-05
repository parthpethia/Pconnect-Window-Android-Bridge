import React from 'react';
import { ShieldCheck, Zap, Download, Play, Monitor, Smartphone, Lock, Send, Wifi } from 'lucide-react';
import './Hero.css';

export default function Hero() {
  return (
    <section className="hero-section">
      <div className="hero-glow"></div>
      
      <div className="hero-container">
        <div className="hero-content">
          <div className="hero-badge">
            <span className="status-pill">
              <span className="status-dot"></span>
              100% Local LAN • Zero Cloud Infrastructure
            </span>
          </div>

          <h1 className="hero-title">
            Seamless PC Control,<br />
            <span className="gradient-text">Lightning Fast</span> Latency
          </h1>

          <p className="hero-subtitle">
            Pconnect bridges your Android smartphone directly to your Windows PC over Wi-Fi. Experience real-time screen streaming, remote touch, system controls, and instant text input with 0 cloud middleman.
          </p>

          <div className="hero-cta-group">
            <a href="#download" className="btn-hero-primary">
              <Download size={20} />
              <span>Download Free</span>
            </a>
            <a href="#demo" className="btn-hero-secondary">
              <Play size={18} />
              <span>Try Interactive Demo</span>
            </a>
          </div>

          <div className="hero-highlights">
            <div className="highlight-item">
              <ShieldCheck size={18} color="var(--accent-emerald)" />
              <span>TOFU PIN Security</span>
            </div>
            <div className="highlight-item">
              <Zap size={18} color="var(--accent-cyan)" />
              <span>WebSocket & TLS</span>
            </div>
            <div className="highlight-item">
              <Wifi size={18} color="var(--accent-light)" />
              <span>UDP Auto-Discovery</span>
            </div>
          </div>
        </div>

        {/* Visual Mockup showing live phone & PC sync */}
        <div className="hero-visual">
          <div className="device-frame-wrapper">
            <div className="pc-mockup">
              <div className="pc-window-header">
                <div className="win-dot red"></div>
                <div className="win-dot yellow"></div>
                <div className="win-dot green"></div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <img src="/logo-pconnect.png" alt="Pconnect Logo" className="app-header-logo" />
                  <span className="pc-window-title">Pconnect Agent v1.0.0 (Windows)</span>
                </div>
              </div>

              <div className="pc-screen-content">
                <div className="pc-status-card">
                  <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                    <Monitor size={22} color="var(--accent-light)" />
                    <div>
                      <div style={{ fontWeight: 700, fontSize: '0.95rem' }}>DESKTOP-LAN-NODE</div>
                      <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>192.168.1.105:47821</div>
                    </div>
                  </div>
                  <span style={{ fontSize: '0.75rem', background: 'rgba(16, 185, 129, 0.2)', color: '#10b981', padding: '4px 8px', borderRadius: '4px', fontWeight: 700 }}>
                    ACTIVE
                  </span>
                </div>

                <div style={{ textAlign: 'center', margin: '4px 0' }}>
                  <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginBottom: '6px' }}>ROTATING PAIRING PIN</div>
                  <div className="pc-pin-box">749 - 382</div>
                </div>

                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.75rem', color: 'var(--text-secondary)' }}>
                  <span>UDP Broadcast: 47822</span>
                  <span>TLS WSS: 47824</span>
                </div>
              </div>
            </div>

            {/* Floating Mobile Phone Mockup */}
            <div className="phone-floating-mockup">
              <div className="phone-screen">
                <div className="phone-header">
                  <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                    <img src="/logo-pconnect.png" alt="Pconnect Logo" className="app-header-logo" />
                    <span>Pconnect Mobile</span>
                  </div>
                  <span style={{ color: '#10b981' }}>Connected</span>
                </div>

                <div className="phone-action-grid">
                  <div className="phone-btn">
                    <Lock size={16} color="var(--accent-light)" />
                    <span>Lock PC</span>
                  </div>
                  <div className="phone-btn">
                    <Send size={16} color="var(--accent-cyan)" />
                    <span>Input Text</span>
                  </div>
                  <div className="phone-btn">
                    <Monitor size={16} color="var(--accent-amber)" />
                    <span>Remote View</span>
                  </div>
                  <div className="phone-btn">
                    <Wifi size={16} color="var(--accent-emerald)" />
                    <span>Ping: 2ms</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
