import fs from "node:fs";
import path from "node:path";

const DOCS_ROOT = "D:\\天章游戏开发\\docs";
const OUT_DIR = "D:\\天章游戏开发\\chm_build\\html";
const CHM_TITLE = "天章游戏开发 — 设计文档全集";

fs.rmSync(OUT_DIR, { recursive: true, force: true });
fs.mkdirSync(OUT_DIR, { recursive: true });

// ---- Collect all txt files ----
const allFiles = [];
function walk(dir) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const e of entries) {
    const fp = path.join(dir, e.name);
    if (e.isDirectory()) walk(fp);
    else if (e.name.endsWith(".txt") && !e.name.startsWith(".")) {
      allFiles.push({
        fullPath: fp,
        relPath: path.relative(DOCS_ROOT, fp),
        dirName: path.relative(DOCS_ROOT, dir),
        baseName: e.name.replace(/\.txt$/, ""),
      });
    }
  }
}
walk(DOCS_ROOT);
console.log(`Found ${allFiles.length} txt files`);

// ---- HTML conversion helpers ----
function escapeHtml(str) {
  return str.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

function txtToHtmlBody(rawText) {
  const lines = rawText.split(/\r?\n/);
  let html = "", inTable = false, inList = false;
  for (const line of lines) {
    if (/[┌┬┐├┼┤└┴┘╔╗╚╝║═]/.test(line)) {
      if (!inTable) { html += '<pre class="table">'; inTable = true; }
      html += escapeHtml(line) + "\n";
      continue;
    } else if (inTable) { html += "</pre>\n"; inTable = false; }
    if (/^[══╔╗╚╝]{5,}/.test(line)) { html += '<hr class="section-divider">\n'; continue; }
    if (/^[─－—–]{5,}/.test(line)) { html += '<hr>\n'; continue; }
    const hMatch = line.match(/^【(.+?)】/);
    if (hMatch) { html += `<h2>${escapeHtml(hMatch[1])}</h2>\n`; continue; }
    const chMatch = line.match(/^第[一二三四五六七八九十百千]+章[：:]/);
    if (chMatch) { html += `<h3>${escapeHtml(line)}</h3>\n`; continue; }
    const subMatch = line.match(/^([一二三四五六七八九十]+)、/);
    if (subMatch) { html += `<h4>${escapeHtml(line)}</h4>\n`; continue; }
    const kvMatch = line.match(/^(\S{2,10}[：:])\s*(.+)/);
    if (kvMatch && line.length < 120) {
      html += `<p class="kv"><span class="key">${escapeHtml(kvMatch[1])}</span>${escapeHtml(kvMatch[2])}</p>\n`;
      continue;
    }
    if (/^[\s]*[-•]/.test(line)) {
      if (!inList) { html += "<ul>\n"; inList = true; }
      html += `<li>${escapeHtml(line.replace(/^[\s]*[-•]\s*/, ""))}</li>\n`;
      continue;
    } else if (inList) { html += "</ul>\n"; inList = false; }
    if (line.trim() === "") {
      if (inList) { html += "</ul>\n"; inList = false; }
      html += "<br>\n";
      continue;
    }
    html += `<p>${escapeHtml(line)}</p>\n`;
  }
  if (inList) html += "</ul>\n";
  if (inTable) html += "</pre>\n";
  return html;
}

function wrapHtml(title, body, prevLink, nextLink, upLink) {
  let nav = '<div class="nav">\n';
  if (upLink) nav += `  <a href="${upLink}">▲ 上级目录</a>\n`;
  if (prevLink) nav += `  <a href="${prevLink}">◀ 上一篇</a>\n`;
  if (nextLink) nav += `  <a href="${nextLink}">下一篇 ▶</a>\n`;
  nav += '</div>\n';
  return `<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.0 Transitional//EN">
<html>
<head>
<meta http-equiv="Content-Type" content="text/html; charset=UTF-8">
<title>${escapeHtml(title)}</title>
<style>
  body { font-family: "Microsoft YaHei", "SimSun", sans-serif; max-width: 900px; margin: 20px auto; padding: 0 20px; background: #f5f0e8; color: #333; line-height: 1.8; }
  h2 { color: #8b0000; border-bottom: 2px solid #8b0000; padding-bottom: 6px; margin-top: 30px; }
  h3 { color: #444; margin-top: 24px; }
  h4 { color: #666; margin-top: 18px; }
  hr { border: none; border-top: 1px solid #ccc; margin: 20px 0; }
  hr.section-divider { border-top: 3px double #8b0000; margin: 30px 0; }
  pre.table { background: #fff; border: 1px solid #ccc; padding: 10px; overflow-x: auto; font-family: "SimSun", "Microsoft YaHei", monospace; font-size: 14px; line-height: 1.5; white-space: pre; }
  p.kv { margin: 2px 0; }
  p.kv .key { color: #8b0000; font-weight: bold; display: inline-block; min-width: 100px; }
  ul { padding-left: 24px; }
  li { margin: 2px 0; }
  .nav { background: #fff; border: 1px solid #ddd; padding: 8px 16px; margin-bottom: 20px; display: flex; gap: 16px; font-size: 14px; }
  .nav a { color: #8b0000; text-decoration: none; }
  .nav a:hover { text-decoration: underline; }
  .footer { margin-top: 40px; padding-top: 10px; border-top: 1px solid #ccc; font-size: 12px; color: #999; text-align: center; }
</style>
</head>
<body>
${nav}
${body}
<div class="footer">天章游戏开发 — 设计文档全集 | 由构建脚本自动生成</div>
</body>
</html>`;
}

// ---- Generate HTML files ----
const fileIndex = {};
console.log("Generating HTML files...");
for (const f of allFiles) {
  const raw = fs.readFileSync(f.fullPath, "utf8");
  let title = f.baseName;
  const titleMatch = raw.match(/【(.+?)】/);
  if (titleMatch) title = titleMatch[1];
  const htmlFile = f.relPath.replace(/\\/g, "/").replace(/\.txt$/, ".html");
  const outPath = path.join(OUT_DIR, htmlFile);
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  const body = txtToHtmlBody(raw);
  fs.writeFileSync(outPath, wrapHtml(title, body, null, null, "../index.html"), "utf8");
  fileIndex[f.relPath] = { title, htmlFile, dirName: f.dirName };
}

// Build per-directory ordering for nav links
const dirFiles = {};
for (const f of allFiles) {
  const d = f.dirName || ".";
  if (!dirFiles[d]) dirFiles[d] = [];
  dirFiles[d].push(f);
}
for (const [, files] of Object.entries(dirFiles)) {
  for (let i = 0; i < files.length; i++) {
    const info = fileIndex[files[i].relPath];
    info.prev = i > 0 ? fileIndex[files[i - 1].relPath].htmlFile : null;
    info.next = i < files.length - 1 ? fileIndex[files[i + 1].relPath].htmlFile : null;
  }
}

console.log("Fixing navigation...");
for (const f of allFiles) {
  const info = fileIndex[f.relPath];
  const raw = fs.readFileSync(f.fullPath, "utf8");
  const body = txtToHtmlBody(raw);
  const upLink = "../index.html";
  const prevLink = info.prev ? path.relative(path.dirname(info.htmlFile), info.prev).replace(/\\/g, "/") : null;
  const nextLink = info.next ? path.relative(path.dirname(info.htmlFile), info.next).replace(/\\/g, "/") : null;
  fs.writeFileSync(path.join(OUT_DIR, info.htmlFile), wrapHtml(info.title, body, prevLink, nextLink, upLink), "utf8");
}

// ---- Generate index.html with proper tree ----
console.log("Generating index.html...");

// Build directory tree
const tree = { name: ".", files: [], children: {} };
for (const f of allFiles) {
  const parts = f.dirName ? f.dirName.split(path.sep) : [];
  let node = tree;
  for (const p of parts) {
    if (!node.children[p]) node.children[p] = { name: p, files: [], children: {} };
    node = node.children[p];
  }
  node.files.push(f);
}

function sortKeys(obj) {
  return Object.keys(obj).sort((a, b) => {
    // Sort by priority: top-level dirs in logical order, then alpha
    const order = { "基础设定": 1, "角色养成": 2, "功法": 3, "术法": 4, "神通": 5, "道具": 6, "丹药": 7, "天材地宝": 8, "门派": 9, "地图": 10 };
    if (order[a] && order[b]) return order[a] - order[b];
    if (order[a]) return -1;
    if (order[b]) return 1;
    return a.localeCompare(b, "zh-CN");
  });
}

function renderTreeHtml(node, depth = 0) {
  let html = "";
  const displayName = node.name === "." ? "天章设定" : node.name;
  if (depth > 0) {
    const hLevel = Math.min(depth + 1, 4);
    html += `<h${hLevel}>${escapeHtml(displayName)}</h${hLevel}>\n`;
  }
  if (node.files.length > 0) {
    const sorted = node.files.sort((a, b) => a.baseName.localeCompare(b.baseName, "zh-CN"));
    html += "<ul>\n";
    for (const f of sorted) {
      const info = fileIndex[f.relPath];
      const indent = "  ".repeat(depth);
      html += `${indent}  <li><a href="${info.htmlFile.replace(/\\/g, "/")}">${escapeHtml(info.title)}</a></li>\n`;
    }
    html += "</ul>\n";
  }
  for (const key of sortKeys(node.children)) {
    html += renderTreeHtml(node.children[key], depth + 1);
  }
  return html;
}

let tocBody = `<h1>${CHM_TITLE}</h1>\n`;
tocBody += `<p class="kv"><span class="key">文件总数：</span>${allFiles.length}</p>\n`;
tocBody += `<p class="kv"><span class="key">生成时间：</span>${new Date().toLocaleString("zh-CN")}</p>\n`;
tocBody += '<hr>\n';
tocBody += renderTreeHtml(tree, 0);
fs.writeFileSync(path.join(OUT_DIR, "index.html"), wrapHtml(CHM_TITLE, tocBody, null, null, null), "utf8");

// ---- Generate TOC (HHC) with proper nesting ----
console.log("Generating toc.hhc...");
function escXml(s) { return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); }

function renderTocNode(node, depth = 0) {
  let hhc = "";
  const indent = "  ".repeat(depth);
  if (node.name === ".") {
    // Root: just render files directly under root level, skip "(root)" label
    if (node.files.length > 0) {
      const sorted = node.files.sort((a, b) => a.baseName.localeCompare(b.baseName, "zh-CN"));
      for (const f of sorted) {
        const info = fileIndex[f.relPath];
        hhc += `${indent}<LI><OBJECT type="text/sitemap">\n`;
        hhc += `${indent}  <param name="Name" value="${escXml(info.title)}">\n`;
        hhc += `${indent}  <param name="Local" value="${info.htmlFile.replace(/\\/g, "/")}">\n`;
        hhc += `${indent}</OBJECT>\n`;
      }
    }
  } else {
    hhc += `${indent}<LI><OBJECT type="text/sitemap">\n`;
    hhc += `${indent}  <param name="Name" value="${escXml(node.name)}">\n`;
    hhc += `${indent}  <param name="Local" value="index.html">\n`;
    hhc += `${indent}</OBJECT>\n`;
    hhc += `${indent}<UL>\n`;
    // Files in this directory
    if (node.files.length > 0) {
      const sorted = node.files.sort((a, b) => a.baseName.localeCompare(b.baseName, "zh-CN"));
      for (const f of sorted) {
        const info = fileIndex[f.relPath];
        hhc += `${indent}  <LI><OBJECT type="text/sitemap">\n`;
        hhc += `${indent}    <param name="Name" value="${escXml(info.title)}">\n`;
        hhc += `${indent}    <param name="Local" value="${info.htmlFile.replace(/\\/g, "/")}">\n`;
        hhc += `${indent}  </OBJECT>\n`;
      }
    }
    // Subdirectories
    for (const key of sortKeys(node.children)) {
      hhc += renderTocNode(node.children[key], depth + 2);
    }
    hhc += `${indent}</UL>\n`;
  }
  return hhc;
}

let hhcBody = `<!DOCTYPE HTML PUBLIC "-//IETF//DTD HTML//EN">
<HTML>
<HEAD>
<meta name="GENERATOR" content="build_chm_v3.mjs">
</HEAD>
<BODY>
<OBJECT type="text/site properties">
  <param name="Window Styles" value="0x800025">
</OBJECT>
<UL>
  <LI><OBJECT type="text/sitemap">
    <param name="Name" value="${escXml(CHM_TITLE)}">
    <param name="Local" value="index.html">
  </OBJECT>
  <UL>
`;

// Root-level files
if (tree.files.length > 0) {
  const sorted = tree.files.sort((a, b) => a.baseName.localeCompare(b.baseName, "zh-CN"));
  for (const f of sorted) {
    const info = fileIndex[f.relPath];
    hhcBody += `    <LI><OBJECT type="text/sitemap">\n`;
    hhcBody += `      <param name="Name" value="${escXml(info.title)}">\n`;
    hhcBody += `      <param name="Local" value="${info.htmlFile.replace(/\\/g, "/")}">\n`;
    hhcBody += `    </OBJECT>\n`;
  }
}

// Top-level directories with proper nesting
for (const key of sortKeys(tree.children)) {
  hhcBody += renderTocNode(tree.children[key], 2);
}

hhcBody += `  </UL>
</UL>
</BODY>
</HTML>`;
fs.writeFileSync(path.join(OUT_DIR, "toc.hhc"), hhcBody, "utf8");

// ---- Generate HHP ----
console.log("Generating project.hhp...");
let hhpFiles = "";
for (const f of allFiles) {
  hhpFiles += fileIndex[f.relPath].htmlFile.replace(/\\/g, "/") + "\n";
}
hhpFiles += "index.html\n";
const hhp = `[OPTIONS]
Compatibility=1.1 or later
Compiled file=../天章设计文档全集.chm
Contents file=toc.hhc
Default topic=index.html
Display compile progress=No
Language=0x804 中文(中国)
Title=${CHM_TITLE}

[FILES]
${hhpFiles}

[INFOTYPES]
`;
fs.writeFileSync(path.join(OUT_DIR, "project.hhp"), hhp, "utf8");

console.log(`\nDone! ${allFiles.length} HTML + index.html + project.hhp + toc.hhc`);
console.log(`Output: ${OUT_DIR}`);
console.log(`TOC: proper nested tree structure`);
