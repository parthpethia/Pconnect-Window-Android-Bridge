import React, { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Sun, Moon, Download, Menu, X, Shield, Cpu, Activity, Sparkles } from 'lucide-react';
import './Navbar.css';

export default function Navbar({ theme, toggleTheme }) {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const location = useLocation();

  const isHome = location.pathname === '/';

  return (
    <header className="navbar glass-panel">
      <div className="navbar-inner">
        <Link to="/" className="brand-link" onClick={() => setMobileMenuOpen(false)}>
          <img src="/logo-pconnect.png" alt="Pconnect Logo" className="brand-logo" />
          <span className="brand-name">Pconnect</span>
        </Link>

        <ul className={`nav-links ${mobileMenuOpen ? 'mobile-open' : ''}`}>
          {isHome ? (
            <>
              <li><a href="#how-it-works" className="nav-link" onClick={() => setMobileMenuOpen(false)}>How It Works</a></li>
              <li><a href="#demo" className="nav-link" onClick={() => setMobileMenuOpen(false)}>Live Demo</a></li>
              <li><a href="#showcase" className="nav-link" onClick={() => setMobileMenuOpen(false)}>App Gallery</a></li>
              <li><a href="#features" className="nav-link" onClick={() => setMobileMenuOpen(false)}>Features</a></li>
              <li><a href="#architecture" className="nav-link" onClick={() => setMobileMenuOpen(false)}>Architecture</a></li>
            </>
          ) : (
            <li><Link to="/" className="nav-link" onClick={() => setMobileMenuOpen(false)}>Home</Link></li>
          )}
          <li>
            <Link 
              to="/privacy" 
              className={`nav-link ${location.pathname === '/privacy' ? 'active' : ''}`}
              onClick={() => setMobileMenuOpen(false)}
            >
              Privacy Policy
            </Link>
          </li>
        </ul>

        <div className="nav-actions">
          <button 
            className="theme-toggle-btn" 
            onClick={toggleTheme}
            aria-label="Toggle light and dark mode"
            title={`Switch to ${theme === 'dark' ? 'Light' : 'Dark'} Mode`}
          >
            {theme === 'dark' ? <Sun size={19} /> : <Moon size={19} />}
          </button>

          {isHome && (
            <a href="#download" className="btn-primary">
              <Download size={17} />
              <span>Get App</span>
            </a>
          )}

          <button 
            className="mobile-menu-btn"
            onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
            aria-label="Toggle Navigation Menu"
          >
            {mobileMenuOpen ? <X size={24} /> : <Menu size={24} />}
          </button>
        </div>
      </div>
    </header>
  );
}
