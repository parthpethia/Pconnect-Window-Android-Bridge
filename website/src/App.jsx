import React, { useState, useEffect } from 'react';
import { BrowserRouter as Router, Routes, Route, useLocation } from 'react-router-dom';
import Navbar from './components/Navbar';
import Footer from './components/Footer';
import Home from './pages/Home';
import Privacy from './pages/Privacy';
import MobileRemote from './pages/MobileRemote';
import './App.css';

function MainLayout() {
  const [theme, setTheme] = useState(() => {
    return localStorage.getItem('pconnect_theme') || 'dark';
  });

  const location = useLocation();
  const isRemoteRoute = location.pathname === '/pair' || location.pathname === '/remote';

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('pconnect_theme', theme);
  }, [theme]);

  const toggleTheme = () => {
    setTheme(prev => (prev === 'dark' ? 'light' : 'dark'));
  };

  if (isRemoteRoute) {
    return (
      <Routes>
        <Route path="/pair" element={<MobileRemote />} />
        <Route path="/remote" element={<MobileRemote />} />
      </Routes>
    );
  }

  return (
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
  );
}

export default function App() {
  return (
    <Router>
      <MainLayout />
    </Router>
  );
}
