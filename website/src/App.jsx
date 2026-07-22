import React, { useState, useEffect } from 'react';
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';
import Navbar from './components/Navbar';
import Footer from './components/Footer';
import Home from './pages/Home';
import Privacy from './pages/Privacy';
import './App.css';

export default function App() {
  const [theme, setTheme] = useState(() => {
    return localStorage.getItem('pconnect_theme') || 'dark';
  });

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('pconnect_theme', theme);
  }, [theme]);

  const toggleTheme = () => {
    setTheme(prev => (prev === 'dark' ? 'light' : 'dark'));
  };

  return (
    <Router>
      <div className="app-container">
        <Navbar theme={theme} toggleTheme={toggleTheme} />
        <main className="main-content">
          <Routes>
            <Route path="/" element={<Home />} />
            <Route path="/privacy" element={<Privacy />} />
            <Route path="*" element={<Home />} />
          </Routes>
        </main>
        <Footer />
      </div>
    </Router>
  );
}
