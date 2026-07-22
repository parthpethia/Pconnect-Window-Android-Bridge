import React, { useState } from 'react';
import { Radio, Search, KeyRound, Zap, ArrowRight, ShieldCheck, CheckCircle2 } from 'lucide-react';
import './HowItWorks.css';

export default function HowItWorks() {
  const [activeStep, setActiveStep] = useState(0);

  const steps = [
    {
      num: 1,
      title: "1. UDP LAN Discovery",
      short: "PC broadcasts UDP beacon on port 47822.",
      icon: <Radio size={24} color="var(--accent-cyan)" />,
      heading: "Zero-Configuration UDP Broadcast",
      description: "When the Windows Pconnect Agent runs in your system tray, it continuously emits a lightweight UDP broadcast packet on port 47822 across your local network segment. No router port forwarding or cloud server is needed.",
      code: `// Windows Agent (C# .NET 8)
UdpClient udpServer = new UdpClient(47822);
byte[] beacon = Encoding.UTF8.GetBytes("PCONNECT_DISCOVERY|DESKTOP-NODE|47821|47824");
await udpServer.SendAsync(beacon, beacon.Length, new IPEndPoint(IPAddress.Broadcast, 47822));`,
      details: ["Port 47822 UDP Broadcast", "Instant PC Name & IP resolution", "LAN bound & router firewall friendly"]
    },
    {
      num: 2,
      title: "2. Android Auto-Detect",
      short: "App captures beacon & lists available PCs.",
      icon: <Search size={24} color="var(--accent-light)" />,
      heading: "Automatic Device Scanning",
      description: "The Flutter Android app listens on the local Wi-Fi subnet for incoming Pconnect discovery beacons. Found computers appear instantly in the connect screen with low-latency ping metrics and IP details.",
      code: `// Android App (Flutter / Dart)
final RawDatagramSocket socket = await RawDatagramSocket.bind(InternetAddress.anyIPv4, 47822);
socket.listen((event) {
  Datagram? dg = socket.receive();
  if (dg != null) parseDiscoveredHost(dg);
});`,
      details: ["Instant network scan", "Manual IP override option", "Auto-reconnect memory"]
    },
    {
      num: 3,
      title: "3. TOFU PIN Pairing",
      short: "Pair using rotating PIN code for secret token.",
      icon: <KeyRound size={24} color="var(--accent-amber)" />,
      heading: "Trust-On-First-Use (TOFU) Authentication",
      description: "To connect, enter the 6-digit PIN displayed on your PC's system tray. Upon validation, the PC generates a cryptographic per-device token saved locally in %AppData%\\Pconnect\\paired-devices.json. Future sessions connect seamlessly without re-typing PINs.",
      code: `// Mutual TOFU Security Token Exchange
{
  "type": "PAIR_REQUEST",
  "code": "749382",
  "device_name": "Galaxy S24 Ultra",
  "device_id": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d"
}
// Response -> Returns session token saved on Android device`,
      details: ["Rotating 6-digit PIN protection", "Local device token persistence", "Revoke paired devices anytime"]
    },
    {
      num: 4,
      title: "4. Real-time WS Control",
      short: "Low-latency WebSocket & TLS screen/input stream.",
      icon: <Zap size={24} color="var(--accent-emerald)" />,
      heading: "Ultra Low-Latency WebSocket Channel",
      description: "Control commands (mouse clicks, lock screen, keystrokes, screen frame streaming) flow over high-performance binary WebSockets on port 47821 or encrypted TLS on port 47824, yielding sub-10ms response times.",
      code: `// Real-Time Action Payload (TCP 47821 / TLS 47824)
{
  "action": "LOCK_PC",
  "timestamp": 1721665900,
  "token": "sec_tok_894bf29a..."
}
// PC Agent executes LockWorkStation() instantly!`,
      details: ["WebSocket / TLS encryption", "<10ms response latency", "Support for remote screen capture & input"]
    }
  ];

  const current = steps[activeStep];

  return (
    <section id="how-it-works" className="section-container how-it-works-section">
      <div className="section-header">
        <span className="status-pill" style={{ marginBottom: '16px' }}>
          Architecture & Workflow
        </span>
        <h2>How Pconnect Works Under the Hood</h2>
        <p>A transparent, high-performance architecture built for security, speed, and 100% local operation.</p>
      </div>

      <div className="steps-timeline">
        {steps.map((s, idx) => (
          <div 
            key={idx} 
            className={`step-card ${activeStep === idx ? 'active' : ''}`}
            onClick={() => setActiveStep(idx)}
          >
            <div className="step-num">{s.num}</div>
            <h3>{s.title}</h3>
            <p>{s.short}</p>
          </div>
        ))}
      </div>

      <div className="step-detail-box">
        <div className="step-detail-info">
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '12px' }}>
            {current.icon}
            <span style={{ fontSize: '0.85rem', fontWeight: 700, color: 'var(--text-muted)' }}>STEP {current.num} OF 4</span>
          </div>
          <h4>{current.heading}</h4>
          <p>{current.description}</p>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
            {current.details.map((item, i) => (
              <div key={i} style={{ display: 'flex', alignItems: 'center', gap: '10px', fontSize: '0.9rem', color: 'var(--text-primary)' }}>
                <CheckCircle2 size={16} color="var(--accent-emerald)" />
                <span>{item}</span>
              </div>
            ))}
          </div>
        </div>

        <div className="step-detail-code">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
            <span style={{ fontSize: '0.8rem', fontWeight: 600, color: 'var(--text-muted)' }}>PROTOCOL SPECIFICATION</span>
            <span className="packet-pill">LAN WebSocket / UDP</span>
          </div>
          <pre className="code-snippet-box">
            <code>{current.code}</code>
          </pre>
        </div>
      </div>
    </section>
  );
}
