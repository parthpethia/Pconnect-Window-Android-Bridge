import { motion, useInView } from 'framer-motion'
import { useRef } from 'react'
import './HowItWorks.css'

const steps = [
  {
    num: '01',
    title: 'Install',
    desc: 'Download the Windows agent on your PC and the Android app on your phone. Both are lightweight and install in seconds.',
  },
  {
    num: '02',
    title: 'Pair',
    desc: 'Open both apps on the same Wi-Fi. Your phone auto-discovers the PC. Enter the 6-digit pairing code shown on your tray icon.',
  },
  {
    num: '03',
    title: 'Control',
    desc: 'That\'s it. Your phone is now a remote for your PC. Mouse, keyboard, files, clipboard — everything works instantly.',
  },
]

export default function HowItWorks() {
  const headerRef = useRef(null)
  const headerInView = useInView(headerRef, { once: true, margin: '-80px' })

  return (
    <section id="how-it-works" className="how-it-works">
      <div className="how-it-works__inner">
        <motion.div
          ref={headerRef}
          className="section-header"
          initial={{ opacity: 0, y: 40 }}
          animate={headerInView ? { opacity: 1, y: 0 } : {}}
          transition={{ duration: 0.7, ease: [0.22, 1, 0.36, 1] }}
        >
          <p className="section-eyebrow">Simple Setup</p>
          <h2 className="section-title-serif">
            Three steps.<br />That's it.
          </h2>
        </motion.div>
        <div className="how-it-works__grid">
          {steps.map((step, i) => (
            <StepCard key={step.num} step={step} index={i} isLast={i === steps.length - 1} />
          ))}
        </div>
      </div>
    </section>
  )
}

function StepCard({ step, index, isLast }) {
  const ref = useRef(null)
  const isInView = useInView(ref, { once: true, margin: '-60px' })

  return (
    <>
      <motion.div
        ref={ref}
        className="step-card"
        initial={{ opacity: 0, y: 50, scale: 0.95 }}
        animate={isInView ? { opacity: 1, y: 0, scale: 1 } : {}}
        transition={{
          duration: 0.6,
          delay: index * 0.15,
          ease: [0.22, 1, 0.36, 1],
        }}
      >
        <motion.div
          className="step-card__number"
          initial={{ opacity: 0, scale: 0.5 }}
          animate={isInView ? { opacity: 1, scale: 1 } : {}}
          transition={{
            duration: 0.5,
            delay: index * 0.15 + 0.2,
            ease: [0.22, 1, 0.36, 1],
          }}
        >
          {step.num}
        </motion.div>
        <h3 className="step-card__title">{step.title}</h3>
        <p className="step-card__desc">{step.desc}</p>
      </motion.div>
      {!isLast && (
        <motion.div
          className="step-connector"
          initial={{ opacity: 0, scaleX: 0 }}
          animate={isInView ? { opacity: 1, scaleX: 1 } : {}}
          transition={{ duration: 0.5, delay: index * 0.15 + 0.3 }}
        >
          <svg width="80" height="2" viewBox="0 0 80 2">
            <line x1="0" y1="1" x2="80" y2="1" stroke="currentColor" strokeWidth="1" strokeDasharray="6 4" />
          </svg>
        </motion.div>
      )}
    </>
  )
}
