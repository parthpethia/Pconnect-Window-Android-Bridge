import React, { useState } from 'react';
import { Lock, Unlock, Keyboard, Monitor, VolumeX, Volume2, Send, Terminal, Wifi, CheckCircle2 } from 'lucide-react';
import './InteractiveDemo.css';

export default function InteractiveDemo() {
  const [activeState, setActiveState] = useState('idle'); // idle, locked, typing, streaming, muted
  const [inputText, setInputText] = useState('Hello from Android!');
  const [lastLog, setLastLog] = useState('WS 47821: Connected [192.168.1.105]');

  const handleAction = (type, logMessage) => {
    setActiveState(type);
    setLastLog(`Payload -> ${logMessage}`);
  };

  const handleSendText = () => {
    if (!inputText) return;
    setActiveState('typing');
    setLastLog(`TEXT_INPUT: "${inputText}" sent over WebSocket`);
  };

  return (
    <section id="demo" className="section-container demo-section">
      <div className="section-header">
        <span className="status-pill" style={{ marginBottom: '16px' }}>
          Live Playground
        </span>
        <h2>Try Pconnect Interactive Simulator</h2>
        <p>Click buttons on the virtual smartphone to experience how PC actions respond instantly with zero latency.</p>
      </div>

      <div className="demo-stage">
        {/* Virtual Phone Controller */}
        <div className="phone-controller">
          <div className="phone-screen-inner">
            <div className="phone-top-bar">
              <span style={{ fontWeight: 700, color: 'var(--accent-light)' }}>Pconnect App</span>
              <span style={{ color: 'var(--accent-emerald)', display: 'flex', alignItems: 'center', gap: '4px' }}>
                <Wifi size={12} /> 2ms
              </span>
            </div>

            <div className="phone-actions-grid">
              <button 
                className={`demo-action-btn ${activeState === 'locked' ? 'active-btn' : ''}`}
                onClick={() => handleAction(activeState === 'locked' ? 'idle' : 'locked', activeState === 'locked' ? 'PC UNLOCKED' : 'LOCK_WORKSTATION')}
              >
                {activeState === 'locked' ? <Unlock size={20} /> : <Lock size={20} />}
                <span>{activeState === 'locked' ? 'Unlock PC' : 'Lock PC'}</span>
              </button>

              <button 
                className={`demo-action-btn ${activeState === 'streaming' ? 'active-btn' : ''}`}
                onClick={() => handleAction('streaming', 'START_SCREEN_CAPTURE (60 FPS Stream)')}
              >
                <Monitor size={20} />
                <span>Screen Share</span>
              </button>

              <button 
                className={`demo-action-btn ${activeState === 'muted' ? 'active-btn' : ''}`}
                onClick={() => handleAction(activeState === 'muted' ? 'idle' : 'muted', activeState === 'muted' ? 'AUDIO UNMUTED' : 'SYSTEM_MUTE_TOGGLE')}
              >
                {activeState === 'muted' ? <Volume2 size={20} /> : <VolumeX size={20} />}
                <span>{activeState === 'muted' ? 'Unmute' : 'Mute Audio'}</span>
              </button>

              <button 
                className={`demo-action-btn ${activeState === 'idle' ? 'active-btn' : ''}`}
                onClick={() => handleAction('idle', 'STATUS_CHECK ping = 1.8ms')}
              >
                <CheckCircle2 size={20} />
                <span>System Ready</span>
              </button>
            </div>

            <div style={{ marginTop: '8px' }}>
              <label style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-muted)', marginBottom: '4px', display: 'block' }}>
                Remote Text Input:
              </label>
              <div style={{ display: 'flex', gap: '6px' }}>
                <input 
                  type="text" 
                  className="text-input-field" 
                  value={inputText}
                  onChange={(e) => setInputText(e.target.value)}
                  placeholder="Type to send to PC..."
                />
                <button className="demo-action-btn" onClick={handleSendText} style={{ padding: '8px 12px' }}>
                  <Send size={16} />
                </button>
              </div>
            </div>
          </div>
        </div>

        {/* Virtual Windows PC Monitor */}
        <div className="pc-monitor-display">
          <div className="pc-top-nav">
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <Terminal size={14} color="var(--accent-light)" />
              <span style={{ fontSize: '0.75rem', color: 'var(--text-secondary)' }}>Windows Desktop Agent</span>
            </div>
            <span style={{ fontSize: '0.7rem', color: 'var(--accent-emerald)' }}>● LAN Active</span>
          </div>

          <div className="pc-viewport">
            {activeState === 'locked' && (
              <div className="pc-screen-locked">
                <Lock size={56} color="var(--accent-rose)" />
                <h3 style={{ fontSize: '1.4rem' }}>Windows Workstation Locked</h3>
                <p style={{ fontSize: '0.85rem', color: 'var(--text-muted)' }}>Locked instantly via remote Pconnect command.</p>
              </div>
            )}

            {activeState === 'typing' && (
              <div className="pc-screen-typing">
                <span style={{ fontSize: '0.8rem', color: 'var(--text-muted)', display: 'block', marginBottom: '8px' }}>RECEIVED TEXT STREAM:</span>
                "{inputText}"
              </div>
            )}

            {activeState === 'streaming' && (
              <div className="pc-screen-streaming">
                <Monitor size={48} color="var(--accent-primary)" />
                <span style={{ fontWeight: 700, fontSize: '1.1rem' }}>Streaming Screen Frame Buffer</span>
                <span style={{ fontSize: '0.8rem', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)' }}>Resolution: 1920x1080 • FPS: 60 • Protocol: WSS</span>
              </div>
            )}

            {activeState === 'muted' && (
              <div style={{ textAlign: 'center', color: 'var(--accent-amber)' }}>
                <VolumeX size={52} />
                <h4 style={{ fontSize: '1.2rem', marginTop: '12px' }}>System Audio Muted</h4>
              </div>
            )}

            {activeState === 'idle' && (
              <div style={{ textAlign: 'center' }}>
                <div style={{ fontSize: '3rem', fontWeight: 800, color: 'rgba(255,255,255,0.08)' }}>PCONNECT</div>
                <p style={{ color: 'var(--text-secondary)', fontSize: '0.95rem', marginTop: '12px' }}>
                  Ready to receive remote actions from Android device over LAN.
                </p>
              </div>
            )}
          </div>

          <div className="event-log-ticker">
            <span>{lastLog}</span>
            <span style={{ color: 'var(--text-muted)' }}>0ms latency</span>
          </div>
        </div>
      </div>
    </section>
  );
}
