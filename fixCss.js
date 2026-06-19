const fs = require('fs');
const path = 'd:\\\\downdown\\\\last\\\\portfolio website\\\\src\\\\index.css';
let css = fs.readFileSync(path, 'utf-8');

// #ffffff 80% should be changed to #111827 80% (dark gray text)
css = css.split('#ffffff 80%').join('#111827 80%');

// Also need to check other #ffffff usage
css = css.split('linear-gradient(135deg, #ffffff 30%').join('linear-gradient(135deg, #111827 30%');

fs.writeFileSync(path, css);
console.log("index.css updated!");
