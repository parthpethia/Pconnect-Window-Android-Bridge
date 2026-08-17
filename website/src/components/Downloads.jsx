import React from 'react';
import { Monitor, Smartphone, Download, CheckCircle2, ShieldAlert } from 'lucide-react';
import './Downloads.css';

export default function Downloads() {
  return (
    <section id="download" className="section-container downloads-section">
      <div className="section-header">
        <span className="status-pill" style={{ marginBottom: '16px' }}>
          Free & Open
        </span>
        <h2>Get Pconnect Today</h2>
        <p>Download the Windows Desktop Agent and Android App to start controlling your PC over Wi-Fi.</p>
      </div>

      <div className="downloads-grid">
        <div className="download-card">
          <div className="download-icon">
            <Monitor size={32} />
          </div>
          <h3>Windows Desktop Agent</h3>
          <p>Runs quietly in your system tray on Windows 10 & 11. Handles UDP broadcast discovery and WebSocket control channel.</p>
          <span className="req-badge">Requires .NET 8 Runtime • Windows 10/11</span>
          <a 
            href="/releases/Pconnect.Agent.exe" 
            download="Pconnect.Agent.exe"
            className="btn-primary"
            style={{ width: '100%', justifyContent: 'center', padding: '14px' }}
          >
            <Download size={18} />
            <span>Download Agent (.exe)</span>
          </a>
        </div>

        <div className="download-card">
          <div className="download-icon" style={{ background: 'rgba(0, 206, 201, 0.15)', color: 'var(--accent-cyan)' }}>
            <Smartphone size={32} />
          </div>
          <h3>Android Mobile App</h3>
          <p>Fast, intuitive Flutter interface for auto-discovering PCs, pairing via PIN code, remote screen view, and trackpad input.</p>
          <span className="req-badge">Android 8.0 (API 26) or higher</span>
          <a 
            href="/releases/Pconnect.apk" 
            download="Pconnect.apk"
            className="btn-primary"
            style={{ width: '100%', justifyContent: 'center', padding: '14px', background: 'linear-gradient(135deg, #00cec9 0%, #6c5ce7 100%)' }}
          >
            <Download size={18} />
            <span>Download APK (.apk)</span>
          </a>
        </div>
      </div>
    </section>
  );
}
