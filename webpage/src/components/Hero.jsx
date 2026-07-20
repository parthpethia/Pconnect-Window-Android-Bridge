import { motion } from 'framer-motion'
import './Hero.css'

export default function Hero() {
  return (
    <section id="hero" className="hero">
      {/* Animated background grid */}
      <div className="hero__grid" />

      {/* Floating glow orbs */}
      <motion.div
        className="hero__glow hero__glow--1"
        animate={{
          x: [0, 40, 0],
          y: [0, -30, 0],
          scale: [1, 1.15, 1],
        }}
        transition={{ duration: 8, repeat: Infinity, ease: 'easeInOut' }}
      />
      <motion.div
        className="hero__glow hero__glow--2"
        animate={{
          x: [0, -35, 0],
          y: [0, 25, 0],
          scale: [1, 1.1, 1],
        }}
        transition={{ duration: 10, repeat: Infinity, ease: 'easeInOut' }}
      />
      <motion.div
        className="hero__glow hero__glow--3"
        animate={{
          x: [0, 20, 0],
          y: [0, 40, 0],
          scale: [1, 1.2, 1],
        }}
        transition={{ duration: 12, repeat: Infinity, ease: 'easeInOut' }}
      />

      {/* Floating particles */}
      <div className="hero__particles">
        {Array.from({ length: 20 }).map((_, i) => (
          <motion.div
            key={i}
            className="hero__particle"
            style={{
              left: `${Math.random() * 100}%`,
              top: `${Math.random() * 100}%`,
              width: `${2 + Math.random() * 3}px`,
              height: `${2 + Math.random() * 3}px`,
            }}
            animate={{
              y: [0, -30 - Math.random() * 40, 0],
              opacity: [0.1, 0.6, 0.1],
            }}
            transition={{
              duration: 4 + Math.random() * 6,
              repeat: Infinity,
              ease: 'easeInOut',
              delay: Math.random() * 4,
            }}
          />
        ))}
      </div>

      {/* Content */}
      <div className="hero__content">
        <motion.p
          className="hero__eyebrow"
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.7, delay: 0.2, ease: [0.22, 1, 0.36, 1] }}
        >
          Local. Wireless. Instant.
        </motion.p>

        <motion.h1
          className="hero__title"
          initial={{ opacity: 0, y: 40 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.8, delay: 0.4, ease: [0.22, 1, 0.36, 1] }}
        >
          <span className="hero__title-serif">Control your</span>
          <br />
          <motion.span
            className="hero__title-bold"
            initial={{ letterSpacing: '20px', opacity: 0 }}
            animate={{ letterSpacing: '6px', opacity: 1 }}
            transition={{ duration: 1.2, delay: 0.6, ease: [0.22, 1, 0.36, 1] }}
          >
            WINDOWS PC
          </motion.span>
          <br />
          <span className="hero__title-serif">from Android.</span>
        </motion.h1>

        <motion.p
          className="hero__subtitle"
          initial={{ opacity: 0, y: 30 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.7, delay: 0.8, ease: [0.22, 1, 0.36, 1] }}
        >
          Zero cloud. Zero accounts. Just your phone and your PC
          <br className="hero__br-desktop" /> on the same Wi-Fi.
        </motion.p>

        <motion.div
          className="hero__actions"
          initial={{ opacity: 0, y: 30 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.7, delay: 1.0, ease: [0.22, 1, 0.36, 1] }}
        >
          <motion.a
            href="#download"
            className="hero__btn hero__btn--primary"
            whileHover={{ scale: 1.04, y: -2 }}
            whileTap={{ scale: 0.97 }}
          >
            <span>Download Now</span>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
              <path d="M7 17l9.2-9.2M17 17V7H7" />
            </svg>
          </motion.a>
          <motion.a
            href="#features"
            className="hero__btn hero__btn--ghost"
            whileHover={{ scale: 1.04 }}
            whileTap={{ scale: 0.97 }}
          >
            Explore Features
          </motion.a>
        </motion.div>
      </div>

      {/* Scroll indicator */}
      <motion.div
        className="hero__scroll"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 1.5, duration: 0.8 }}
      >
        <span>Scroll</span>
        <motion.div
          className="hero__scroll-line"
          animate={{ scaleY: [1, 1.3, 1], opacity: [0.4, 1, 0.4] }}
          transition={{ duration: 2, repeat: Infinity, ease: 'easeInOut' }}
        />
      </motion.div>
    </section>
  )
}
