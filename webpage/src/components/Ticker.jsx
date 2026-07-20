import './Ticker.css'

const items = [
  'Mouse Control', 'Virtual Keyboard', 'Screen Mirroring', 'File Transfer',
  'Clipboard Sync', 'Volume Control', 'Brightness', 'Lock PC',
  'App Launcher', 'Media Keys', 'Notifications', 'Shutdown',
  'Key Combos', 'Audit Logs', 'Custom Commands',
]

export default function Ticker() {
  // Duplicate items for seamless loop
  const doubled = [...items, ...items]

  return (
    <section className="ticker">
      <div className="ticker__track">
        <div className="ticker__content">
          {doubled.map((item, i) => (
            <span key={i}>
              <span className="ticker__item">{item}</span>
              <span className="ticker__dot">●</span>
            </span>
          ))}
        </div>
      </div>
    </section>
  )
}
