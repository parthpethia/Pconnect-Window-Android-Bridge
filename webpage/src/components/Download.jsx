import { motion, useInView } from 'framer-motion'
import { useRef } from 'react'
import './Download.css'

export default function Download() {
  const headerRef = useRef(null)
  const headerInView = useInView(headerRef, { once: true, margin: '-80px' })
  const cardsRef = useRef(null)
  const cardsInView = useInView(cardsRef, { once: true, margin: '-60px' })

  return (
    <section id="download" className="download">
      <div className="download__inner">
        <motion.div
          ref={headerRef}
          className="section-header"
          initial={{ opacity: 0, y: 40 }}
          animate={headerInView ? { opacity: 1, y: 0 } : {}}
          transition={{ duration: 0.7, ease: [0.22, 1, 0.36, 1] }}
        >
          <p className="section-eyebrow">Get Started</p>
          <h2 className="section-title-serif download__title">Your Turn.</h2>
        </motion.div>
        <div ref={cardsRef} className="download__grid">
          <motion.div
            className="download__card"
            initial={{ opacity: 0, y: 40 }}
            animate={cardsInView ? { opacity: 1, y: 0 } : {}}
            transition={{ duration: 0.6, ease: [0.22, 1, 0.36, 1] }}
            whileHover={{ y: -4 }}
          >
            <span className="download__label">THE HOST</span>
            <h3 className="download__platform">Windows</h3>
            <p className="download__req">Windows 10+ / .NET 8</p>
            <div className="download__spacer" />
            <motion.a
              href="/releases/Pconnect.Agent.exe"
              download="Pconnect.Agent.exe"
              className="download__action"
              whileHover={{ x: 4 }}
            >
              <span>Download .EXE</span>
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M5 12h14M12 5l7 7-7 7" />
              </svg>
            </motion.a>
          </motion.div>
          <motion.div
            className="download__card"
            initial={{ opacity: 0, y: 40 }}
            animate={cardsInView ? { opacity: 1, y: 0 } : {}}
            transition={{ duration: 0.6, delay: 0.12, ease: [0.22, 1, 0.36, 1] }}
            whileHover={{ y: -4 }}
          >
            <span className="download__label">THE REMOTE</span>
            <h3 className="download__platform">Android</h3>
            <p className="download__req">Android 8.0+</p>
            <div className="download__spacer" />
            <motion.a
              href="/releases/Pconnect.apk"
              download="Pconnect.apk"
              className="download__action"
              whileHover={{ x: 4 }}
            >
              <span>Download .APK</span>
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M7 17l9.2-9.2M17 17V7H7" />
              </svg>
            </motion.a>
          </motion.div>
        </div>
      </div>
    </section>
  )
}
