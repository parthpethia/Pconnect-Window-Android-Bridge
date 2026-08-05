import React, { useState, useRef, useEffect } from 'react';
import { Peer } from 'peerjs';
import { QRCodeSVG } from 'qrcode.react';
import { 
  Lock, Unlock, Keyboard, Monitor, VolumeX, Volume2, Send, Terminal, Wifi, 
  CheckCircle2, MousePointer, ShieldCheck, RefreshCw, Cpu, Zap, Activity, QrCode, Smartphone, Edit3 
} from 'lucide-react';
import './InteractiveDemo.css';

// Stable Session ID so QR code never breaks on re-render
const getStableSessionId = () => {
  let id = sessionStorage.getItem('pconnect_pc_session');
  if (!id) {
    id = 'pconnect-' + Math.floor(1000 + Math.random() * 9000);
    sessionStorage.setItem('pconnect_pc_session', id);
  }
  return id;
};

export default function InteractiveDemo() {
  const [activeTab, setActiveTab] = useState('qr'); // qr, trackpad, typing, actions
  const [activeState, setActiveState] = useState('idle'); // idle, locked, typing, streaming, muted
  const [inputText, setInputText] = useState('Hello from Pconnect Android!');
  const [typedOutput, setTypedOutput] = useState('Hello from Pconnect Android!');
  
  // Trackpad cursor position
  const [cursorPos, setCursorPos] = useState({ x: 50, y: 50 }); // percentage

  // Click ripple effect state
  const [clickRipple, setClickRipple] = useState(null); // { x, y, button, id }

  // WebRTC PeerJS Host state
  const [sessionId] = useState(getStableSessionId);
  const [peerConnected, setPeerConnected] = useState(false);
  const [connectedDevice, setConnectedDevice] = useState('');
  
  // Host IP / Domain state for LAN Wi-Fi QR Code
  const [hostIp, setHostIp] = useState(() => {
    const hostname = window.location.hostname;
    if (hostname !== 'localhost' && hostname !== '127.0.0.1') {
      return hostname;
    }
    return '172.19.208.210'; // Detected LAN Wi-Fi IP
  });
  const [editingIp, setEditingIp] = useState(false);

  const peerRef = useRef(null);

  // Packet log stream
  const [logs, setLogs] = useState([
    { id: 1, type: 'UDP', text: 'BEACON broadcast on 255.255.255.255:47822', latency: '0.4ms' },
    { id: 2, type: 'WSS', text: 'TLS 1.3 Handshake completed with DESKTOP-LAN', latency: '1.2ms' },
    { id: 3, type: 'AUTH', text: 'Session Token verified (TOFU Storage)', latency: '0.9ms' }
  ]);

  const addLog = (type, text, latency = '1.8ms') => {
    setLogs(prev => [
      { id: Date.now(), type, text, latency },
      ...prev.slice(0, 3)
    ]);
  };

  // Attempt WebRTC Local IP Auto-Discovery
  useEffect(() => {
    try {
      const pc = new RTCPeerConnection({ iceServers: [] });
      pc.createDataChannel('');
      pc.createOffer().then(offer => pc.setLocalDescription(offer));
      pc.onicecandidate = (ice) => {
        if (ice && ice.candidate && ice.candidate.candidate) {
          const match = ice.candidate.candidate.match(/(?:[0-9]{1,3}\.){3}[0-9]{1,3}/);
          if (match && match[0] && !match[0].startsWith('127.')) {
            setHostIp(match[0]);
          }
        }
      };
    } catch (e) {
      console.log('IP discovery fallback:', e);
    }
  }, []);

  // Initialize Event Streams
  useEffect(() => {
    const handleDataPacket = (data) => {
      if (!data) return;
      setPeerConnected(true);

      if (data.type === 'MOVE') {
        setCursorPos({ x: data.x, y: data.y });
        addLog('MOVE', `0x04 MOUSE_POS {x: ${Math.round(data.x * 19.2)}, y: ${Math.round(data.y * 10.8)}}`, '1.2ms');
      } else if (data.type === 'TEXT') {
        setTypedOutput(data.text);
        setActiveState('typing');
        addLog('TEXT', `0x08 KEY_STREAM: "${data.text.slice(-15)}"`, '1.5ms');
      } else if (data.type === 'CMD') {
        setActiveState(data.state || data.action);
        addLog('CMD', `0x02 REMOTE_ACTION: ${data.action.toUpperCase()}`, '1.1ms');
      } else if (data.type === 'CLICK') {
        setClickRipple({ x: cursorPos.x, y: cursorPos.y, button: data.button || 'left', id: Date.now() });
        addLog('CLICK', `0x05 MOUSE_${(data.button || 'left').toUpperCase()}_CLICK`, '0.8ms');
        setTimeout(() => setClickRipple(null), 600);
      } else if (data.type === 'PING') {
        addLog('PING', `LAN Bridge Ping pong round-trip OK`, '0.6ms');
      }
    };

    // 1. LAN Server-Sent Events (SSE) stream
    let sse = null;
    try {
      sse = new EventSource('/api/events-stream');
      sse.onmessage = (e) => {
        try {
          const data = JSON.parse(e.data);
          handleDataPacket(data);
        } catch (err) {}
      };
    } catch (err) {}

    // 2. PeerJS WebRTC setup
    const peer = new Peer(sessionId, {
      config: {
        iceServers: [
          { urls: 'stun:stun.l.google.com:19302' },
          { urls: 'stun:stun1.l.google.com:19302' }
        ]
      }
    });
    peerRef.current = peer;

    peer.on('open', (id) => {
      addLog('AUTH', `WebRTC Signal Server Ready: ${id}`);
    });

    peer.on('connection', (conn) => {
      setPeerConnected(true);
      setConnectedDevice(conn.peer || 'Remote Phone');
      addLog('AUTH', `PHONE CONNECTED: WebRTC DataChannel [${conn.peer}]`, '0.9ms');

      conn.on('data', handleDataPacket);

      conn.on('close', () => {
        addLog('AUTH', 'Remote Phone Connection Closed');
      });
    });

    // 3. BroadcastChannel & LocalStorage fallback
    let bc = null;
    if (typeof BroadcastChannel !== 'undefined') {
      bc = new BroadcastChannel('pconnect_channel_' + sessionId);
      bc.onmessage = (e) => handleDataPacket(e.data);
    }

    const handleStorageEvent = (e) => {
      if (e.key === 'pconnect_event_' + sessionId && e.newValue) {
        try {
          const data = JSON.parse(e.newValue);
          handleDataPacket(data);
        } catch (err) {}
      }
    };
    window.addEventListener('storage', handleStorageEvent);

    return () => {
      if (sse) sse.close();
      if (peerRef.current) peerRef.current.destroy();
      if (bc) bc.close();
      window.removeEventListener('storage', handleStorageEvent);
    };
  }, [sessionId, cursorPos.x, cursorPos.y]);

  const port = window.location.port || '3000';
  const protocol = window.location.protocol;
  const qrUrl = `${protocol}//${hostIp}:${port}/pair?session=${sessionId}`;

  // Handle trackpad movement on web sandbox
  const handleTrackpadMove = (e) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const x = Math.max(5, Math.min(95, ((e.clientX - rect.left) / rect.width) * 100));
    const y = Math.max(5, Math.min(95, ((e.clientY - rect.top) / rect.height) * 100));
    
    setCursorPos({ x, y });
    addLog('MOVE', `0x04 MOUSE_ABS {x: ${Math.round(x * 19.2)}, y: ${Math.round(y * 10.8)}}`, '1.4ms');
  };

  const handleAction = (type, logMessage) => {
    setActiveState(prev => (prev === type ? 'idle' : type));
    addLog('CMD', `0x0${type === 'locked' ? '2' : type === 'muted' ? '3' : '5'} ${logMessage}`);
  };

  const handleSendText = (textVal) => {
    setInputText(textVal);
    setTypedOutput(textVal);
    setActiveState('typing');
    addLog('TEXT', `0x08 KEY_STREAM: "${textVal.slice(-15)}"`);
  };

  return (
    <section id="demo" className="section-container demo-section">
      <div className="section-header">
        <span className="status-pill" style={{ marginBottom: '16px' }}>
          <Zap size={14} color="var(--accent-light)" /> Zero-Installation WebRTC Bridge
        </span>
        <h2>Scan QR Code to Control PC from Your Real Phone</h2>
        <p>No app installation required! Scan the QR code below with your smartphone camera (connected to the same Wi-Fi network) to establish a live remote control bridge.</p>
      </div>

      <div className="demo-stage">
        {/* Virtual Phone Controller */}
        <div className="phone-controller">
          <div className="phone-bezel-top">
            <div className="phone-camera-dot"></div>
          </div>

          <div className="phone-screen-inner">
            {/* Phone App Status Bar */}
            <div className="phone-top-bar">
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                <img src="/logo-pconnect.png" alt="Logo" style={{ width: '16px', height: '16px' }} />
                <span style={{ fontWeight: 700, color: 'var(--text-primary)' }}>Pconnect App</span>
              </div>
              <span className={`phone-connection-tag ${peerConnected ? 'connected' : 'scanning'}`}>
                <Wifi size={11} /> {peerConnected ? 'Live Real Phone' : '1.8 ms'}
              </span>
            </div>

            {/* Controller Mode Tabs */}
            <div className="controller-tabs">
              <button 
                className={`tab-btn ${activeTab === 'qr' ? 'active' : ''}`}
                onClick={() => setActiveTab('qr')}
              >
                <QrCode size={13} /> Scan QR
              </button>
              <button 
                className={`tab-btn ${activeTab === 'trackpad' ? 'active' : ''}`}
                onClick={() => { setActiveTab('trackpad'); setActiveState('idle'); }}
              >
                <MousePointer size={13} /> Touchpad
              </button>
              <button 
                className={`tab-btn ${activeTab === 'typing' ? 'active' : ''}`}
                onClick={() => { setActiveTab('typing'); setActiveState('typing'); }}
              >
                <Keyboard size={13} /> Text Stream
              </button>
              <button 
                className={`tab-btn ${activeTab === 'actions' ? 'active' : ''}`}
                onClick={() => setActiveTab('actions')}
              >
                <Zap size={13} /> Actions
              </button>
            </div>

            {/* TAB CONTENT: QR Code Scanner */}
            {activeTab === 'qr' && (
              <div className="qr-code-tab-container">
                <div className="qr-code-wrapper">
                  {sessionId ? (
                    <QRCodeSVG value={qrUrl} size={135} bgColor="#ffffff" fgColor="#090c14" level="M" />
                  ) : (
                    <RefreshCw size={24} className="spin-icon" color="var(--accent-light)" />
                  )}
                </div>
                <div style={{ textAlign: 'center', marginTop: '6px', width: '100%' }}>
                  <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--accent-cyan)' }}>
                    Scan with Phone Camera (Same Wi-Fi)
                  </span>

                  <div className="lan-ip-bar">
                    <span style={{ fontSize: '0.65rem', color: 'var(--text-muted)' }}>Wi-Fi IP:</span>
                    {editingIp ? (
                      <input 
                        type="text" 
                        value={hostIp} 
                        onChange={(e) => setHostIp(e.target.value)}
                        onBlur={() => setEditingIp(false)}
                        className="ip-edit-input"
                        autoFocus
                      />
                    ) : (
                      <span className="ip-text" onClick={() => setEditingIp(true)}>
                        {hostIp}:{port} <Edit3 size={10} />
                      </span>
                    )}
                  </div>

                  <p style={{ fontSize: '0.65rem', color: 'var(--text-muted)', marginTop: '4px', wordBreak: 'break-all' }}>
                    Or open on phone: <br />
                    <a href={qrUrl} target="_blank" rel="noopener noreferrer" style={{ color: 'var(--accent-light)' }}>
                      {qrUrl}
                    </a>
                  </p>
                </div>
              </div>
            )}

            {/* TAB CONTENT: Trackpad */}
            {activeTab === 'trackpad' && (
              <div className="trackpad-container">
                <div 
                  className="trackpad-surface"
                  onMouseMove={handleTrackpadMove}
                  onTouchMove={(e) => {
                    const touch = e.touches[0];
                    handleTrackpadMove(touch);
                  }}
                >
                  <MousePointer size={22} color="var(--accent-light)" className="trackpad-hint-icon" />
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>
                    Drag finger here to move PC cursor live
                  </span>
                  <div className="trackpad-coords">
                    X: {Math.round(cursorPos.x * 19.2)} | Y: {Math.round(cursorPos.y * 10.8)}
                  </div>
                </div>
                <div className="trackpad-buttons">
                  <button className="click-btn" onClick={() => {
                    setClickRipple({ x: cursorPos.x, y: cursorPos.y, button: 'left', id: Date.now() });
                    addLog('CLICK', '0x05 MOUSE_LEFT_CLICK');
                    setTimeout(() => setClickRipple(null), 600);
                  }}>Left Click</button>

                  <button className="click-btn" onClick={() => {
                    setClickRipple({ x: cursorPos.x, y: cursorPos.y, button: 'right', id: Date.now() });
                    addLog('CLICK', '0x06 MOUSE_RIGHT_CLICK');
                    setTimeout(() => setClickRipple(null), 600);
                  }}>Right Click</button>
                </div>
              </div>
            )}

            {/* TAB CONTENT: Remote Text */}
            {activeTab === 'typing' && (
              <div className="phone-typing-box">
                <label className="input-label">Type text to send to PC in real-time:</label>
                <textarea 
                  className="text-input-field" 
                  rows="3"
                  value={inputText}
                  onChange={(e) => handleSendText(e.target.value)}
                  placeholder="Type anything here..."
                />
                <button 
                  className="btn-send-text"
                  onClick={() => handleSendText(inputText)}
                >
                  <Send size={13} /> Send Stream to PC Notepad
                </button>
              </div>
            )}

            {/* TAB CONTENT: Quick Actions */}
            {activeTab === 'actions' && (
              <div className="phone-actions-grid">
                <button 
                  className={`demo-action-btn ${activeState === 'locked' ? 'active-btn' : ''}`}
                  onClick={() => handleAction('locked', activeState === 'locked' ? 'UNLOCK_WORKSTATION' : 'LOCK_WORKSTATION')}
                >
                  {activeState === 'locked' ? <Unlock size={18} /> : <Lock size={18} />}
                  <span>{activeState === 'locked' ? 'Unlock PC' : 'Lock PC'}</span>
                </button>

                <button 
                  className={`demo-action-btn ${activeState === 'streaming' ? 'active-btn' : ''}`}
                  onClick={() => handleAction('streaming', 'START_60FPS_MIRROR')}
                >
                  <Monitor size={18} />
                  <span>Screen Mirror</span>
                </button>

                <button 
                  className={`demo-action-btn ${activeState === 'muted' ? 'active-btn' : ''}`}
                  onClick={() => handleAction('muted', activeState === 'muted' ? 'UNMUTE_SYSTEM' : 'MUTE_SYSTEM')}
                >
                  {activeState === 'muted' ? <Volume2 size={18} /> : <VolumeX size={18} />}
                  <span>{activeState === 'muted' ? 'Unmute' : 'Mute Audio'}</span>
                </button>

                <button 
                  className={`demo-action-btn ${activeState === 'idle' ? 'active-btn' : ''}`}
                  onClick={() => { setActiveState('idle'); addLog('PING', '0x00 PING latency=1.2ms'); }}
                >
                  <CheckCircle2 size={18} />
                  <span>System Ping</span>
                </button>
              </div>
            )}
          </div>
        </div>

        {/* Virtual Windows PC Monitor Display */}
        <div className="pc-monitor-display">
          {/* Windows Title Bar */}
          <div className="pc-top-nav">
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <Terminal size={14} color="var(--accent-light)" />
              <span style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-secondary)' }}>
                Windows Workstation Monitor • Pconnect LAN Host
              </span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
              {peerConnected ? (
                <span className="real-phone-connected-badge">
                  <Smartphone size={12} color="#10b981" /> Real Phone Paired Live!
                </span>
              ) : (
                <span style={{ fontSize: '0.7rem', color: 'var(--accent-emerald)', display: 'flex', alignItems: 'center', gap: '4px' }}>
                  <span className="live-dot"></span> Session: {sessionId}
                </span>
              )}
            </div>
          </div>

          {/* Monitor Viewport Screen */}
          <div className="pc-viewport">
            {/* Click Ripple Indicator */}
            {clickRipple && (
              <div 
                className={`click-ripple-effect ${clickRipple.button}`}
                style={{ left: `${clickRipple.x}%`, top: `${clickRipple.y}%` }}
              >
                <span className="click-ripple-label">{clickRipple.button.toUpperCase()} CLICK</span>
              </div>
            )}

            {/* Live Cursor Indicator */}
            <div 
              className="pc-live-cursor" 
              style={{ left: `${cursorPos.x}%`, top: `${cursorPos.y}%` }}
            >
              <MousePointer size={18} color="#00cec9" />
              <span className="cursor-tag">{peerConnected ? 'Live Mobile Phone' : 'Virtual Controller'}</span>
            </div>

            {/* Screen State 1: Locked */}
            {activeState === 'locked' && (
              <div className="pc-screen-locked">
                <div className="win-lock-icon-bg">
                  <Lock size={44} color="#ff7675" />
                </div>
                <h3 style={{ fontSize: '1.3rem', color: '#ffffff', margin: '4px 0' }}>Workstation Locked</h3>
                <p style={{ fontSize: '0.85rem', color: 'var(--text-muted)' }}>
                  Locked instantly via Pconnect remote action code <code className="code-inline">0x02</code>
                </p>
                <div className="win-clock-badge">12:35 PM • Tuesday</div>
              </div>
            )}

            {/* Screen State 2: Text Stream Notepad */}
            {activeState === 'typing' && (
              <div className="pc-notepad-window">
                <div className="notepad-titlebar">
                  <span>Untitled - Notepad (Receiving Pconnect Remote Stream)</span>
                </div>
                <div className="notepad-body">
                  <span className="notepad-text">{typedOutput || 'Type on phone to stream text here...'}</span>
                  <span className="notepad-blinking-cursor">|</span>
                </div>
                <div className="notepad-statusbar">
                  <span>Ln 1, Col {(typedOutput || '').length + 1}</span>
                  <span>100%</span>
                  <span>Windows (CRLF)</span>
                  <span>UTF-8</span>
                </div>
              </div>
            )}

            {/* Screen State 3: Screen Streaming */}
            {activeState === 'streaming' && (
              <div className="pc-screen-streaming">
                <Activity size={40} color="var(--accent-light)" className="pulse-icon" />
                <span style={{ fontWeight: 700, fontSize: '1.1rem', color: '#ffffff' }}>60 FPS Real-time Screen Buffer</span>
                <div className="stream-stats-bar">
                  <span>Res: 1920x1080</span>
                  <span>Codec: LAN Wi-Fi EventStream</span>
                  <span>FPS: 60</span>
                  <span>Latency: 1.8ms</span>
                </div>
              </div>
            )}

            {/* Screen State 4: Muted */}
            {activeState === 'muted' && (
              <div style={{ textAlign: 'center', color: 'var(--accent-amber)' }}>
                <VolumeX size={52} />
                <h4 style={{ fontSize: '1.2rem', marginTop: '12px' }}>System Master Audio Muted</h4>
                <p style={{ fontSize: '0.8rem', color: 'var(--text-muted)' }}>Windows Audio API Endpoint Muted</p>
              </div>
            )}

            {/* Screen State 5: Idle Desktop View */}
            {activeState === 'idle' && (
              <div className="pc-idle-desktop">
                <div className="desktop-logo-bg">PCONNECT</div>
                <div className="desktop-info-card">
                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                    <ShieldCheck size={18} color="var(--accent-emerald)" />
                    <span style={{ fontWeight: 700, fontSize: '0.9rem' }}>
                      {peerConnected ? '⚡ Real Mobile Device Connected Live!' : 'Scan QR Code on Phone to Pair Live'}
                    </span>
                  </div>
                  <p style={{ fontSize: '0.8rem', color: 'var(--text-secondary)' }}>
                    {peerConnected ? (
                      `Receiving Wi-Fi touch & command stream from your smartphone. Move your finger on your phone screen to move the cursor live.`
                    ) : (
                      `Scan the QR code on the phone screen with your real mobile camera (or open http://${hostIp}:${port}/pair?session=${sessionId}), or touch the virtual touchpad on the left.`
                    )}
                  </p>
                </div>
              </div>
            )}
          </div>

          {/* Animated Protocol Inspector Log at bottom of monitor */}
          <div className="protocol-inspector-log">
            <div className="log-header">
              <Terminal size={12} color="var(--accent-light)" />
              <span>LIVE PROTOCOL PACKET LOG:</span>
            </div>
            <div className="log-entries">
              {logs.map((log) => (
                <div key={log.id} className="log-row">
                  <span className={`log-tag ${log.type}`}>{log.type}</span>
                  <span className="log-text">{log.text}</span>
                  <span className="log-time">{log.latency}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
