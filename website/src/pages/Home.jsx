import React from 'react';
import Hero from '../components/Hero';
import HowItWorks from '../components/HowItWorks';
import InteractiveDemo from '../components/InteractiveDemo';
import AppShowcase from '../components/AppShowcase';
import Features from '../components/Features';
import Architecture from '../components/Architecture';
import Downloads from '../components/Downloads';

export default function Home() {
  return (
    <div className="home-page">
      <Hero />
      <HowItWorks />
      <InteractiveDemo />
      <AppShowcase />
      <Features />
      <Architecture />
      <Downloads />
    </div>
  );
}
