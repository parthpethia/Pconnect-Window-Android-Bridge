const fs = require('fs');
const path = 'd:\\\\downdown\\\\last\\\\portfolio website\\\\src\\\\App.jsx';
let app = fs.readFileSync(path, 'utf-8');

app = app.split('stroke="#d4885a"').join('stroke="#3DDC84"');
app = app.split('fill="#e0e0e0"').join('fill="#4b5563"');

fs.writeFileSync(path, app);
console.log("App.jsx updated!");
