// Raster exports of the approved V3 SVG paths and CSS gradients; no generated replacement art.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require(process.env.SHARP_MODULE || 'sharp');
const source = path.resolve(__dirname, '../d12-ui-proposal-v1-20261029/v3-20261030/hud.js');
const target = path.resolve(__dirname, '../../../../src/Assets/Art/UI/Guanzhong/CombatV3');
const svg = (body, w = 72, h = 72) => `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">${body}</svg>`;
async function write(name, body) {
  let asset = sharp(Buffer.from(body));
  if (name.startsWith('icon_')) asset = sharp(Buffer.from(body), {density: 288});
  await asset.png().toFile(path.join(target, name + '.png'));
}
(async () => {
  fs.mkdirSync(target, {recursive: true});
  const paths = fs.readFileSync(source, 'utf8').match(/const icons=\{([\s\S]*?)\n\};/)[1];
  for (const [,name,d] of paths.matchAll(/\s*(\w+):'([^']+)'/g))
    await write('icon_' + name, svg(`<path d="${d}" fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/>`, 32, 32));
  for (const [name, first, last] of [['identity','#0c1b24ec','#0c1b2400'],['status','#0c1b24dc','#0c1b2400'],['dock','#0a1820f2','#0a182000']]) {
    const vertical = name === 'dock';
    await write(name, svg(`<defs><linearGradient id="g" x1="0" y1="${vertical?'1':'0'}" x2="${vertical?'0':'1'}" y2="0"><stop stop-color="${first}"/><stop offset=".7" stop-color="${vertical?'#0a1820cc':'#0c1b24bb'}"/><stop offset="1" stop-color="${last}"/></linearGradient></defs><rect width="256" height="128" fill="url(#g)"/>`,256,128));
  }
  await write('slot', svg('<defs><radialGradient id="g" cx="50%" cy="90%" r="70%"><stop stop-color="#28434b"/><stop offset="1" stop-color="#101f29"/></radialGradient></defs><rect x=".5" y=".5" width="71" height="71" fill="#0d1b22" stroke="#61727666"/><rect x="4.5" y="4.5" width="63" height="63" fill="url(#g)" stroke="#859f9eaa"/><path d="M8 8h56M8 64h56" stroke="#9db3a545"/>'));
  await write('selected', svg('<rect x=".5" y=".5" width="71" height="71" fill="none" stroke="#bba266"/><rect x="5" y="5" width="62" height="62" fill="none" stroke="#f2db99" stroke-width="2"/>'));
  await write('focus', svg('<rect x="1.5" y="1.5" width="69" height="69" fill="none" stroke="white" stroke-width="2"/>'));
  await write('token', svg('<path d="M14 1h44l13 13v44L58 71H14L1 58V14z" fill="#0c2028cc" stroke="#95aaa488"/>'));
  await write('token_current', svg('<path d="M14 1h44l13 13v44L58 71H14L1 58V14z" fill="none" stroke="#d8bd7d" stroke-width="2"/>'));
  await write('panel', svg('<rect width="72" height="72" fill="#0a1922e8"/><path d="M0 .5h72" stroke="#c5b17c88"/><path d="M0 71.5h72" stroke="#c5b17c44"/>'));
  await write('button', svg('<rect x=".5" y=".5" width="71" height="71" rx="1" fill="#10242ce8" stroke="#a9ac8e66"/>'));
  await write('confirm', svg('<rect x=".5" y=".5" width="71" height="71" rx="1" fill="#b7a16e" stroke="#d6bc7e"/>'));
  await write('end', svg('<rect x=".5" y=".5" width="71" height="71" rx="1" fill="#223d3a" stroke="#b49e6a"/>'));
  await write('solid', svg('<rect width="72" height="72" fill="white"/>'));
  await write('vignette', svg('<defs><linearGradient id="v" x2="0" y2="1"><stop stop-color="#071319b8"/><stop offset=".24" stop-color="#07131900"/><stop offset=".73" stop-color="#07131900"/><stop offset=".99" stop-color="#071319ed"/></linearGradient><linearGradient id="h"><stop stop-color="#07131955"/><stop offset=".25" stop-color="#07131900"/><stop offset=".8" stop-color="#07131900"/><stop offset="1" stop-color="#07131944"/></linearGradient></defs><rect width="256" height="256" fill="url(#v)"/><rect width="256" height="256" fill="url(#h)"/>',256,256));
  console.log('Exported approved V3 icons and CSS surfaces to ' + target);
})();
