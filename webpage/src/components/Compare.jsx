import { motion, useInView } from 'framer-motion'
import { useRef } from 'react'
import './Compare.css'

const pconnectRows = [
  { label: 'SETUP', value: 'Install Android & Windows app. That\'s it.' },
  { label: 'LATENCY', value: 'Direct LAN, sub-50 ms typical' },
  { label: 'PROTOCOL', value: 'WebSocket + WebRTC on local Wi-Fi' },
  { label: 'CLOUD', value: 'None — fully offline' },
  { label: 'ACCOUNTS', value: 'None required' },
  { label: 'NETWORK SWITCH', value: 'Auto-reconnects seamlessly' },
]

const otherRows = [
  { label: 'SETUP', value: 'Create accounts, install on both, configure ports, enable remote access, verify email...' },
  { label: 'LATENCY', value: 'Cloud relay adds overhead' },
  { label: 'PROTOCOL', value: 'Routed through external servers' },
  { label: 'CLOUD', value: 'Required — data passes through 3rd party' },
  { label: 'ACCOUNTS', value: 'Mandatory sign-up' },
  { label: 'NETWORK SWITCH', value: 'Session may drop' },
]

export default function Compare() {
  const headerRef = useRef(null)
  const headerInView = useInView(headerRef, { once: true, margin: '-80px' })
  const cardsRef = useRef(null)
  const cardsInView = useInView(cardsRef, { once: true, margin: '-80px' })

  return (
    <section id="compare" className="compare">
      <div className="compare__inner">
        <motion.div
          ref={headerRef}
          className="section-header"
          initial={{ opacity: 0, y: 40 }}
          animate={headerInView ? { opacity: 1, y: 0 } : {}}
          transition={{ duration: 0.7, ease: [0.22, 1, 0.36, 1] }}
        >
          <p className="section-eyebrow">Why Pconnect?</p>
          <h2 className="section-title-serif">
            <em>Pconnect</em> vs the old way
          </h2>
        </motion.div>
        <div ref={cardsRef} className="compare__grid">
          <motion.div
            className="compare__card compare__card--pconnect"
            initial={{ opacity: 0, x: -40 }}
            animate={cardsInView ? { opacity: 1, x: 0 } : {}}
            transition={{ duration: 0.7, ease: [0.22, 1, 0.36, 1] }}
          >
            <div className="compare__badge">PCONNECT</div>
            {pconnectRows.map((row) => (
              <div key={row.label} className="compare__row">
                <div className="compare__label">{row.label}</div>
                <div className="compare__value">{row.value}</div>
              </div>
            ))}
          </motion.div>
          <motion.div
            className="compare__card compare__card--other"
            initial={{ opacity: 0, x: 40 }}
            animate={cardsInView ? { opacity: 1, x: 0 } : {}}
            transition={{ duration: 0.7, delay: 0.15, ease: [0.22, 1, 0.36, 1] }}
          >
            <div className="compare__badge">TRADITIONAL REMOTE APPS</div>
            {otherRows.map((row) => (
              <div key={row.label} className="compare__row">
                <div className="compare__label">{row.label}</div>
                <div className="compare__value">{row.value}</div>
              </div>
            ))}
          </motion.div>
        </div>
      </div>
    </section>
  )
}
