import React, { useState, useEffect, useRef } from 'react';
import { useSearchParams, Link } from 'react-router-dom';
import { Peer } from 'peerjs';
import { 
  Wifi, MousePointer, Keyboard, Lock, Unlock, Volume2, VolumeX, Monitor, 
  Send, ShieldCheck, RefreshCw, AlertCircle, Smartphone, ArrowLeft 
} from 'lucide-react';
import './MobileRemote.css';

export default function MobileRemote() {
  const [searchParams] = useSearchParams();
  const sessionId = searchParams.get('session') || searchParams.get('s');

  const [status, setStatus] = useState('connecting'); // connecting, connected, disconnected, error
  const [activeTab, setActiveTab] = useState('trackpad'); // trackpad, typing, actions
  const [inputText, setInputText] = useState('');
  const [activeState, setActiveState] = useState('idle');

  // Smooth Touch Coordinates & RAF Throttler
  const posRef = useRef({ x: 50, y: 50 });
  const isDraggingRef = useRef(false);
  const lastTouchRef = useRef(null);
  
  // 60 FPS Frame Batcher state
  const pendingMoveRef = useRef(null);
  const animFrameIdRef = useRef(null);

  // Visual touch dot indicator on mobile
  const [touchVisual, setTouchVisual] = useState({ active: false, x: 50, y: 50 });

  const peerRef = useRef(null);
  const connRef = useRef(null);
  const bcRef = useRef(null);

  // 60 FPS RAF loop to batch touch movements smoothly
  useEffect(() => {
    let active = true;

    const loop = () => {
      if (active && pendingMoveRef.current) {
        const moveData = pendingMoveRef.current;
        pendingMoveRef.current = null;
        sendMoveData(moveData);
      }
      if (active) {
        animFrameIdRef.current = requestAnimationFrame(loop);
      }
    };

    animFrameIdRef.current = requestAnimationFrame(loop);

    return () => {
      active = false;
      if (animFrameIdRef.current) cancelAnimationFrame(animFrameIdRef.current);
    };
  }, []);

  // Initialize LAN Bridge Connection
  useEffect(() => {
    if (!sessionId) {
      setStatus('error');
      return;
    }

    setStatus('connected');

    if (typeof BroadcastChannel !== 'undefined') {
      const bc = new BroadcastChannel('pconnect_channel_' + sessionId);
      bcRef.current = bc;
    }

    try {
      const peer = new Peer({
        config: {
          iceServers: [
            { urls: 'stun:stun.l.google.com:19302' },
            { urls: 'stun:stun1.l.google.com:19302' }
          ]
        }
      });
      peerRef.current = peer;

      peer.on('open', (id) => {
        const conn = peer.connect(sessionId, { reliable: true });
        connRef.current = conn;

        conn.on('open', () => {
          setStatus('connected');
          if (navigator.vibrate) navigator.vibrate([50, 50, 50]);
          conn.send({ type: 'PING', timestamp: Date.now() });
        });

        conn.on('error', (err) => console.error('Peer err:', err));
      });
    } catch (err) {
      console.log('PeerJS init fallback:', err);
    }

    return () => {
      if (connRef.current) connRef.current.close();
      if (peerRef.current) peerRef.current.destroy();
      if (bcRef.current) bcRef.current.close();
    };
  }, [sessionId]);

  // Send packet to PC host
  const sendData = (data) => {
    const payload = { ...data, sessionId, timestamp: Date.now() };

    try {
      fetch('/api/send-event', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      }).catch(() => {});
    } catch (e) {}

    if (connRef.current && connRef.current.open) {
      connRef.current.send(payload);
    }
    if (bcRef.current) {
      bcRef.current.postMessage(payload);
    }
    try {
      localStorage.setItem('pconnect_event_' + sessionId, JSON.stringify({ ...payload, _t: Date.now() }));
    } catch (e) {}

    if (navigator.vibrate && data.type !== 'MOVE') {
      navigator.vibrate(15);
    }
  };

  // Dedicated throttled move sender
  const sendMoveData = (moveData) => {
    sendData({ type: 'MOVE', x: moveData.x, y: moveData.y });
  };

  // Trackpad Touch Start
  const handleTouchStart = (e) => {
    isDraggingRef.current = true;
    let clientX, clientY;
    if (e.touches && e.touches[0]) {
      clientX = e.touches[0].clientX;
      clientY = e.touches[0].clientY;
    } else {
      clientX = e.clientX;
      clientY = e.clientY;
    }
    lastTouchRef.current = { x: clientX, y: clientY };

    const rect = e.currentTarget.getBoundingClientRect();
    const touchX = Math.max(0, Math.min(100, ((clientX - rect.left) / rect.width) * 100));
    const touchY = Math.max(0, Math.min(100, ((clientY - rect.top) / rect.height) * 100));

    setTouchVisual({ active: true, x: touchX, y: touchY });

    const x = Math.max(5, Math.min(95, touchX));
    const y = Math.max(5, Math.min(95, touchY));
    posRef.current = { x, y };

    pendingMoveRef.current = { x, y };
  };

  // Trackpad Touch Move
  const handleTouchMove = (e) => {
    if (!isDraggingRef.current || !lastTouchRef.current) return;
    if (e.cancelable) e.preventDefault();

    let clientX, clientY;
    if (e.touches && e.touches[0]) {
      clientX = e.touches[0].clientX;
      clientY = e.touches[0].clientY;
    } else {
      clientX = e.clientX;
      clientY = e.clientY;
    }

    const rect = e.currentTarget.getBoundingClientRect();
    const touchX = Math.max(0, Math.min(100, ((clientX - rect.left) / rect.width) * 100));
    const touchY = Math.max(0, Math.min(100, ((clientY - rect.top) / rect.height) * 100));
    
    setTouchVisual({ active: true, x: touchX, y: touchY });

    // Calculate delta for fluid trackpad inertia
    const dx = (clientX - lastTouchRef.current.x) / rect.width * 100;
    const dy = (clientY - lastTouchRef.current.y) / rect.height * 100;

    lastTouchRef.current = { x: clientX, y: clientY };

    const newX = Math.max(5, Math.min(95, posRef.current.x + dx * 1.5));
    const newY = Math.max(5, Math.min(95, posRef.current.y + dy * 1.5));

    posRef.current = { x: newX, y: newY };
    pendingMoveRef.current = { x: newX, y: newY };
  };

  const handleTouchEnd = () => {
    isDraggingRef.current = false;
    lastTouchRef.current = null;
    setTouchVisual(prev => ({ ...prev, active: false }));
  };

  const handleAction = (actionType) => {
    const newState = activeState === actionType ? 'idle' : actionType;
    setActiveState(newState);
    sendData({ type: 'CMD', action: actionType, state: newState });
  };

  const handleTextChange = (val) => {
    setInputText(val);
    sendData({ type: 'TEXT', text: val });
  };

  return (
    <div className="mobile-remote-page">
      {/* Top Mobile App Bar */}
      <header className="remote-app-bar">
        <div className="bar-left">
          <img src="/logo-pconnect.png" alt="Logo" className="remote-logo" />
          <span className="remote-app-title">Pconnect Remote</span>
        </div>
        <div className={`status-pill-badge ${status}`}>
          <Wifi size={12} />
          <span>{status === 'connected' ? '60 FPS Bridge' : status}</span>
        </div>
      </header>

      {/* Main Remote Interface */}
      <main className="remote-main-content">
        {status === 'connecting' && (
          <div className="status-overlay">
            <RefreshCw size={32} className="spin-icon" color="var(--accent-cyan)" />
            <h3>Connecting to PC...</h3>
            <p style={{ fontSize: '0.8rem', color: 'var(--text-muted)' }}>Establishing WebRTC Session <code>{sessionId}</code></p>
          </div>
        )}

        {status === 'error' && (
          <div className="status-overlay">
            <AlertCircle size={36} color="#ff7675" />
            <h3>Session Connection Error</h3>
            <p style={{ fontSize: '0.85rem', color: 'var(--text-muted)' }}>Could not connect to PC session.</p>
            <Link to="/" className="btn-retry">Return to Homepage</Link>
          </div>
        )}

        {status === 'connected' && (
          <div className="remote-controller-container">
            {/* Mode Switcher Tabs */}
            <nav className="remote-tab-bar">
              <button 
                className={`r-tab ${activeTab === 'trackpad' ? 'active' : ''}`}
                onClick={() => setActiveTab('trackpad')}
              >
                <MousePointer size={18} />
                <span>Trackpad</span>
              </button>
              <button 
                className={`r-tab ${activeTab === 'typing' ? 'active' : ''}`}
                onClick={() => setActiveTab('typing')}
              >
                <Keyboard size={18} />
                <span>Remote Text</span>
              </button>
              <button 
                className={`r-tab ${activeTab === 'actions' ? 'active' : ''}`}
                onClick={() => setActiveTab('actions')}
              >
                <Monitor size={18} />
                <span>Actions</span>
              </button>
            </nav>

            {/* TAB 1: Trackpad */}
            {activeTab === 'trackpad' && (
              <div className="trackpad-wrapper">
                <div 
                  className="mobile-trackpad-surface"
                  onPointerDown={handleTouchStart}
                  onPointerMove={handleTouchMove}
                  onPointerUp={handleTouchEnd}
                  onTouchStart={handleTouchStart}
                  onTouchMove={handleTouchMove}
                  onTouchEnd={handleTouchEnd}
                  style={{ touchAction: 'none' }}
                >
                  {/* Glowing Finger Dot Indicator */}
                  {touchVisual.active && (
                    <div 
                      className="touch-finger-dot"
                      style={{ left: `${touchVisual.x}%`, top: `${touchVisual.y}%` }}
                    />
                  )}

                  <MousePointer size={32} color="var(--accent-cyan)" style={{ opacity: 0.4 }} />
                  <span className="trackpad-instruction">Drag finger here to move cursor smoothly</span>
                </div>
                <div className="mobile-click-bar">
                  <button className="r-click-btn" onClick={() => sendData({ type: 'CLICK', button: 'left' })}>Left Click</button>
                  <button className="r-click-btn" onClick={() => sendData({ type: 'CLICK', button: 'right' })}>Right Click</button>
                </div>
              </div>
            )}

            {/* TAB 2: Remote Text */}
            {activeTab === 'typing' && (
              <div className="mobile-typing-wrapper">
                <label className="r-label">Type text below to stream live to PC screen:</label>
                <textarea 
                  className="r-textarea"
                  rows="5"
                  value={inputText}
                  onChange={(e) => handleTextChange(e.target.value)}
                  placeholder="Type anything here to appear on PC Notepad..."
                />
                <button className="r-send-btn" onClick={() => sendData({ type: 'TEXT', text: inputText })}>
                  <Send size={16} /> Stream to PC Screen
                </button>
              </div>
            )}

            {/* TAB 3: Remote System Actions */}
            {activeTab === 'actions' && (
              <div className="mobile-actions-wrapper">
                <button 
                  className={`r-action-card ${activeState === 'locked' ? 'active' : ''}`}
                  onClick={() => handleAction('locked')}
                >
                  {activeState === 'locked' ? <Unlock size={24} /> : <Lock size={24} />}
                  <span>{activeState === 'locked' ? 'Unlock PC' : 'Lock PC Workstation'}</span>
                </button>

                <button 
                  className={`r-action-card ${activeState === 'streaming' ? 'active' : ''}`}
                  onClick={() => handleAction('streaming')}
                >
                  <Monitor size={24} />
                  <span>Start 60FPS Screen Mirror</span>
                </button>

                <button 
                  className={`r-action-card ${activeState === 'muted' ? 'active' : ''}`}
                  onClick={() => handleAction('muted')}
                >
                  {activeState === 'muted' ? <Volume2 size={24} /> : <VolumeX size={24} />}
                  <span>{activeState === 'muted' ? 'Unmute Audio' : 'Mute System Audio'}</span>
                </button>
              </div>
            )}
          </div>
        )}
      </main>

      <footer className="remote-footer">
        <span>Connected via 60 FPS LAN Bridge • Zero Installation</span>
      </footer>
    </div>
  );
}
