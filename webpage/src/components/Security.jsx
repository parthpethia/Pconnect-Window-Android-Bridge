import { motion, useInView } from 'framer-motion'
import { useRef } from 'react'
import './Security.css'

const items = [
  {
    num: '01',
    label: 'TRANSPORT',
    title: 'Local-Only Communication',
    desc: 'All data stays on your local Wi-Fi network. No cloud servers, no internet required. Your data never leaves your home.',
  },
  {
    num: '02',
    label: 'PAIRING',
    title: 'Rotating Code + Device Token',
    desc: 'Secure device pairing with rotating 6-digit codes. Once paired, a unique device token ensures only authorized devices connect.',
  },
  {
    num: '03',
    label: 'DISCOVERY',
    title: 'UDP Auto-Discovery',
    desc: 'Your phone automatically finds your PC on the network. No manual IP entry needed. Discovery stays within your LAN subnet.',
  },
  {
    num: '04',
    label: 'ACCESS CONTROL',
    title: 'Role-Based Permissions',
    desc: 'Admin, media-only, or read-only roles. Fine-grained control over what each paired device can do. Shutdown requires a separate PIN.',
  },
]

export default function Security() {
  const headingRef = useRef(null)
  const headingInView = useInView(headingRef, { once: true, margin: '-100px' })

  return (
    <section id="security" className="security">
      <div className="security__inner">
        <div className="security__layout">
          <motion.div
            ref={headingRef}
            className="security__heading"
            initial={{ opacity: 0, x: -40 }}
            animate={headingInView ? { opacity: 1, x: 0 } : {}}
            transition={{ duration: 0.8, ease: [0.22, 1, 0.36, 1] }}
          >
            <p className="section-eyebrow">Privacy First</p>
            <h2 className="security__title">
              Defense<br />in Depth.
            </h2>
          </motion.div>
          <div className="security__items">
            {items.map((item, i) => (
              <SecurityItem key={item.num} item={item} index={i} />
            ))}
          </div>
        </div>
      </div>
    </section>
  )
}

function SecurityItem({ item, index }) {
  const ref = useRef(null)
  const isInView = useInView(ref, { once: true, margin: '-60px' })

  return (
    <>
      {index > 0 && <div className="security__divider" />}
      <motion.div
        ref={ref}
        className="security__item"
        initial={{ opacity: 0, y: 30 }}
        animate={isInView ? { opacity: 1, y: 0 } : {}}
        transition={{
          duration: 0.6,
          delay: index * 0.12,
          ease: [0.22, 1, 0.36, 1],
        }}
      >
        <div className="security__item-label">
          {item.num}. {item.label}
        </div>
        <h3 className="security__item-title">{item.title}</h3>
        <p className="security__item-desc">{item.desc}</p>
      </motion.div>
    </>
  )
}
