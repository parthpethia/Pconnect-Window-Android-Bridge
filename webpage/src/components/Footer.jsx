import { motion } from 'framer-motion'
import './Footer.css'

export default function Footer() {
  return (
    <footer className="footer">
      <div className="footer__inner">
        <motion.div
          className="footer__left"
          initial={{ opacity: 0 }}
          whileInView={{ opacity: 1 }}
          viewport={{ once: true }}
          transition={{ duration: 0.6 }}
        >
          <span>&copy; 2026 Pconnect. All rights reserved.</span>
        </motion.div>
        <motion.div
          className="footer__right"
          initial={{ opacity: 0 }}
          whileInView={{ opacity: 1 }}
          viewport={{ once: true }}
          transition={{ duration: 0.6, delay: 0.1 }}
        >
          <a href="#" className="footer__link">Privacy</a>
          <a href="#" className="footer__link">Terms</a>
          <a href="https://github.com" className="footer__link" target="_blank" rel="noopener noreferrer">GitHub</a>
        </motion.div>
      </div>
    </footer>
  )
}
