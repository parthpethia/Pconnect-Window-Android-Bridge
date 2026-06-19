const fs = require('fs');
const appPath = 'd:\\\\downdown\\\\last\\\\portfolio website\\\\src\\\\App.jsx';
const cssPath = 'd:\\\\downdown\\\\last\\\\portfolio website\\\\src\\\\index.css';

// 1. Modify App.jsx
let app = fs.readFileSync(appPath, 'utf-8');

// Insert state
const stateMarker = 'const [isProjectTechnical, setIsProjectTechnical] = useState(false);';
const stateReplacement = stateMarker + '\n  const [isLoading, setIsLoading] = useState(true);\n\n  useEffect(() => {\n    const timer = setTimeout(() => setIsLoading(false), 1000);\n    return () => clearTimeout(timer);\n  }, []);\n';
app = app.replace(stateMarker, stateReplacement);

// Insert early return for splash screen
const returnMarker = 'return (\n    <>\n      {/* ========== NAVBAR ========== */}';
const returnReplacement = 'if (isLoading) {\n    return (\n      <div className="splash-screen">\n        <img src="/TRANSPARENT.png" alt="Logo" className="splash-logo" />\n      </div>\n    );\n  }\n\n  ' + returnMarker;
app = app.replace(returnMarker, returnReplacement);

fs.writeFileSync(appPath, app);

// 2. Modify index.css
let css = fs.readFileSync(cssPath, 'utf-8');

const splashCss = `
/* ===== SPLASH SCREEN ===== */
.splash-screen {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background: #F3F4F6;
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}

.splash-logo {
  width: 250px;
  max-width: 80%;
  height: auto;
  animation: pulseLogo 1s ease-in-out infinite alternate;
}

@keyframes pulseLogo {
  0% { transform: scale(0.95); opacity: 0.8; }
  100% { transform: scale(1.05); opacity: 1; }
}
`;

// Append CSS to the file
css += '\n' + splashCss;

fs.writeFileSync(cssPath, css);

console.log("Splash screen added successfully!");
