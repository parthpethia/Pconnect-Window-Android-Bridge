import React from 'react';
import { 
  ShieldCheck, Zap, Radio, KeyRound, Monitor, MousePointer, 
  Keyboard, FolderSync, Sliders, ScrollText 
} from 'lucide-react';
import './Features.css';

export default function Features() {
  const featureList = [
    {
      icon: <ShieldCheck size={24} />,
      title: "100% Local & Offline",
      description: "Operates exclusively over your local Wi-Fi network. No cloud accounts, external telemetry, or data leaving your LAN.",
      tag: "Security"
    },
    {
      icon: <Zap size={24} />,
      title: "Low-Latency WebSockets",
      description: "Direct binary WebSocket connections (TCP 47821 / TLS 47824) ensure sub-10ms response times for all control actions.",
      tag: "Performance"
    },
    {
      icon: <Radio size={24} />,
      title: "UDP Auto-Discovery",
      description: "Automatic network scanning on UDP port 47822 resolves your Windows PC's local IP address without manual setup.",
      tag: "Networking"
    },
    {
      icon: <KeyRound size={24} />,
      title: "TOFU Mutual Pairing",
      description: "Trust-On-First-Use rotating PIN verification generates secure, persistent device tokens saved locally.",
      tag: "Authentication"
    },
    {
      icon: <Monitor size={24} />,
      title: "Remote Screen Mirroring",
      description: "Stream high-framerate PC desktop video directly to your Android device for effortless remote monitoring.",
      tag: "Display"
    },
    {
      icon: <MousePointer size={24} />,
      title: "Remote Trackpad & Touch",
      description: "Turn your smartphone screen into a precision multi-touch trackpad for intuitive PC cursor navigation.",
      tag: "Control"
    },
    {
      icon: <Keyboard size={24} />,
      title: "Real-time Text Input",
      description: "Type smoothly on your Android keyboard and push text instantly to focused text fields on your Windows PC.",
      tag: "Input"
    },
    {
      icon: <FolderSync size={24} />,
      title: "Bi-directional File Transfer",
      description: "Send files back and forth between PC and smartphone quickly with chunked progress tracking and integrity validation.",
      tag: "Files"
    },
    {
      icon: <Sliders size={24} />,
      title: "System Actions & Power",
      description: "Lock PC workstation, trigger system sleep, control master audio volume, and manage apps remotely.",
      tag: "Macros"
    }
  ];

  return (
    <section id="features" className="section-container">
      <div className="section-header">
        <span className="status-pill" style={{ marginBottom: '16px' }}>
          Capabilities
        </span>
        <h2>Built for Speed, Security & Convenience</h2>
        <p>Explore the full suite of features designed for effortless control of your Windows workstation from Android.</p>
      </div>

      <div className="features-grid">
        {featureList.map((item, index) => (
          <div key={index} className="feature-card">
            <div className="feature-icon-wrapper">
              {item.icon}
            </div>
            <span className="feature-tag">{item.tag}</span>
            <h3>{item.title}</h3>
            <p>{item.description}</p>
          </div>
        ))}
      </div>
    </section>
  );
}
