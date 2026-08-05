import React from 'react';
import { Link } from 'react-router-dom';
import { ShieldCheck, Wifi, Cpu, Github, ExternalLink } from 'lucide-react';
import './Footer.css';

export default function Footer() {
  return (
    <footer className="footer">
      <div className="footer-grid">
        <div className="footer-brand">
          <div className="brand-link">
            <img src="/logo-pconnect.png" alt="Pconnect Logo" className="brand-logo" />
            <span className="brand-name">Pconnect</span>
          </div>
          <p>
            Ultra low-latency, zero-cloud Windows PC remote control system designed for high performance over local Wi-Fi.
          </p>
        </div>

        <div className="footer-col">
          <h4>Navigation</h4>
          <ul>
            <li><Link to="/">Home</Link></li>
            <li><a href="#how-it-works">How It Works</a></li>
            <li><a href="#demo">Live Demo</a></li>
            <li><a href="#features">Features</a></li>
            <li><a href="#download">Download</a></li>
          </ul>
        </div>

        <div className="footer-col">
          <h4>Technology</h4>
          <ul>
            <li><a href="#architecture">.NET 8 Agent</a></li>
            <li><a href="#architecture">Flutter Android App</a></li>
            <li><a href="#architecture">WebSocket & TLS</a></li>
            <li><a href="#architecture">UDP LAN Discovery</a></li>
          </ul>
        </div>

        <div className="footer-col">
          <h4>Privacy & Legal</h4>
          <ul>
            <li><Link to="/privacy">Privacy Policy</Link></li>
            <li><span style={{ color: 'var(--accent-emerald)', fontSize: '0.85rem' }}>✓ 100% Local LAN Only</span></li>
            <li><span style={{ color: 'var(--text-muted)', fontSize: '0.85rem' }}>Zero Telemetry</span></li>
          </ul>
        </div>
      </div>

      <div className="footer-bottom">
        <p>© {new Date().getFullYear()} Pconnect. Built with precision for fast local PC control.</p>
        <div className="footer-bottom-links">
          <Link to="/privacy">Privacy Policy</Link>
          <span>•</span>
          <span>MIT License</span>
        </div>
      </div>
    </footer>
  );
}
