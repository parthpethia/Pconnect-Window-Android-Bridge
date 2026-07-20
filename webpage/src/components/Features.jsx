import { motion } from 'framer-motion'
import { useInView } from 'framer-motion'
import { useRef } from 'react'
import './Features.css'

const features = [
  {
    label: 'REMOTE INPUT',
    title: <>Full Trackpad<br />&amp; Keyboard</>,
    desc: 'Navigate your PC screen with fluid mouse trackpad controls. Type on your PC\'s active window with a full virtual keyboard — zero lag on LAN.',
    icon: (
      <svg width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"><rect x="2" y="6" width="20" height="12" rx="2"/><path d="M6 14h12M10 10h4"/></svg>
    ),
    large: true,
  },
  {
    label: 'LIVE STREAM',
    title: <>Desktop<br />Mirroring</>,
    desc: 'Stream your PC screen in real-time via WebRTC with H.264 encoding. Low latency, high quality preview right on your phone.',
    icon: (
      <svg width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"><rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/></svg>
    ),
  },
  {
    label: 'CLIPBOARD SYNC',
    title: <>Instant<br />Copy-Paste</>,
    desc: 'Seamless automatic clipboard synchronization. Copy on one device, paste on the other — instantly.',
    icon: (
      <svg width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"><rect x="8" y="2" width="8" height="4" rx="1"/><path d="M16 4h2a2 2 0 012 2v14a2 2 0 01-2 2H6a2 2 0 01-2-2V6a2 2 0 012-2h2"/></svg>
    ),
  },
  {
    label: 'FILE TRANSFER',
    title: <>High-Speed<br />File Share</>,
    desc: 'Transfer photos, videos, and documents at LAN speed. No cloud upload, no file size limits. Direct device-to-device.',
    icon: (
      <svg width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><path d="M14 2v6h6M12 18v-6M9 15l3 3 3-3"/></svg>
    ),
  },
  {
    label: 'SYSTEM CONTROL',
    title: <>Volume, Brightness,<br />Lock &amp; Shutdown</>,
    desc: 'Full system control at your fingertips. Adjust volume and brightness, lock your PC, launch apps, media keys, and even shut down — all from your phone.',
    icon: (
      <svg width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z"/></svg>
    ),
    large: true,
  },
]

function FeatureCard({ feature, index }) {
  const ref = useRef(null)
  const isInView = useInView(ref, { once: true, margin: '-80px' })

  return (
    <motion.div
      ref={ref}
      className={`feature-card ${feature.large ? 'feature-card--large' : ''}`}
      initial={{ opacity: 0, y: 50 }}
      animate={isInView ? { opacity: 1, y: 0 } : {}}
      transition={{
        duration: 0.6,
        delay: index * 0.1,
        ease: [0.22, 1, 0.36, 1],
      }}
      whileHover={{ y: -4 }}
    >
      <div className="feature-card__accent-line" />
      <span className="feature-card__label">{feature.label}</span>
      <h3 className="feature-card__title">{feature.title}</h3>
      <p className="feature-card__desc">{feature.desc}</p>
      <div className="feature-card__icon">{feature.icon}</div>
    </motion.div>
  )
}

export default function Features() {
  const headerRef = useRef(null)
  const headerInView = useInView(headerRef, { once: true, margin: '-80px' })

  return (
    <section id="features" className="features">
      <div className="features__inner">
        <motion.div
          ref={headerRef}
          className="section-header"
          initial={{ opacity: 0, y: 40 }}
          animate={headerInView ? { opacity: 1, y: 0 } : {}}
          transition={{ duration: 0.7, ease: [0.22, 1, 0.36, 1] }}
        >
          <p className="section-eyebrow">Capabilities</p>
          <h2 className="section-title-serif">See it in action.</h2>
        </motion.div>
        <div className="features__grid">
          {features.map((f, i) => (
            <FeatureCard key={f.label} feature={f} index={i} />
          ))}
        </div>
      </div>
    </section>
  )
}
