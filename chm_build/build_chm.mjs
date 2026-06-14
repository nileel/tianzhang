import fs from "node:fs";
import path from "node:path";

const DOCS_ROOT = "D:\\天章游戏开发\\docs";
const OUT_DIR = "D:\\天章游戏开发\\chm_build\\html";
const CHM_TITLE = "天章游戏开发 — 设计文档全集";

// Ensure output dir
fs.rmSync(OUT_DIR, { recursive: true, force: true });
fs.mkdirSync(OUT_DIR, { recursive: true });

// Collect all txt files with their relative paths
const allFiles = [];
function walk(dir) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const e of entries) {
    const fp = path.join(dir, e.name);
    if (e.isDirectory()) {
      walk(fp);
    } else if (e.name.endsWith(".txt") && !e.name.startsWith(".")) {
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

// Build directory tree for TOC
const dirTree = {};
for (const f of allFiles) {
  const parts = f.dirName.split(path.sep);
  let node = dirTree;
  for (const p of parts) {
    if (!node[p]) node[p] = { __files: [] };
    node = node[p];
  }
  node.__files.push(f);
}

// Convert txt content to HTML body
function txtToHtmlBody(rawText) {
  const lines = rawText.split(/\r?\n/);
  let html = "";
  let inTable = false;
  let inList = false;

  for (const line of lines) {
    // Box-drawing table detection
    if (/[┌┬┐├┼┤└┴┘╔╗╚╝║═]/.test(line)) {
      if (!inTable) { html += '<pre class="table">'; inTable = true; }
      html += escapeHtml(line) + "\n";
      continue;
    } else if (inTable) {
      html += "</pre>\n";
      inTable = false;
    }

    // Separator lines
    if (/^[══╔╗╚╝]{5,}/.test(line)) {
      html += '<hr class="section-divider">\n';
      continue;
    }

    // Horizontal rule
    if (/^[─－—–]{5,}/.test(line)) {
      html += '<hr>\n';
      continue;
    }

    // Headers: 【Title】
    const hMatch = line.match(/^【(.+?)】/);
    if (hMatch) {
      html += `<h2>${escapeHtml(hMatch[1])}</h2>\n`;
      continue;
    }

    // Chapter markers like "第一章：xxx"
    const chMatch = line.match(/^第[一二三四五六七八九十百千]+章[：:]/);
    if (chMatch) {
      html += `<h3>${escapeHtml(line)}</h3>\n`;
      continue;
    }

    // Sub-headers like "一、xxx" or "1. xxx"
    const subMatch = line.match(/^([一二三四五六七八九十]+)、/);
    if (subMatch) {
      html += `<h4>${escapeHtml(line)}</h4>\n`;
      continue;
    }

    // Key-value pairs with colon (design fields)
    const kvMatch = line.match(/^(\S{2,10}[：:])\s*(.+)/);
    if (kvMatch && line.length < 120) {
      html += `<p class="kv"><span class="key">${escapeHtml(kvMatch[1])}</span>${escapeHtml(kvMatch[2])}</p>\n`;
      continue;
    }

    // List items
    if (/^[\s]*[-•]/.test(line)) {
      if (!inList) { html += "<ul>\n"; inList = true; }
      const content = line.replace(/^[\s]*[-•]\s*/, "");
      html += `<li>${escapeHtml(content)}</li>\n`;
      continue;
    } else if (inList) {
      html += "</ul>\n";
      inList = false;
    }

    // Empty line
    if (line.trim() === "") {
      if (inList) { html += "</ul>\n"; inList = false; }
      html += "<br>\n";
      continue;
    }

    // Regular line
    html += `<p>${escapeHtml(line)}</p>\n`;
  }

  if (inList) html += "</ul>\n";
  if (inTable) html += "</pre>\n";

  return html;
}

function escapeHtml(str) {
  return str
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

// Generate HTML wrapper
function wrapHtml(title, body, prevLink, nextLink, upLink) {
  let nav = '<div class="nav">\n';
  if (upLink) nav += `  <a href="${upLink}">▲ 上级目录</a>\n`;
  if (prevLink) nav += `  <a href="${prevLink}">◀ 上一篇</a>\n`;
  if (nextLink) nav += `  <a href="${nextLink}">下一篇 ▶</a>\n`;
  nav += '</div>\n';

  return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
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

// Generate all HTML files and build TOC data
const tocData = []; // { level, title, file, children }
const fileIndex = {}; // relPath -> { title, htmlFile, prev, next, up }

// First pass: generate all HTML files
console.log("Generating HTML files...");
for (let i = 0; i < allFiles.length; i++) {
  const f = allFiles[i];
  const raw = fs.readFileSync(f.fullPath, "utf8");
  const body = txtToHtmlBody(raw);

  // Extract title from first 【...】 header
  let title = f.baseName;
  const titleMatch = raw.match(/【(.+?)】/);
  if (titleMatch) title = titleMatch[1];

  const htmlFile = f.relPath.replace(/\\/g, "/").replace(/\.txt$/, ".html");
  const outPath = path.join(OUT_DIR, htmlFile);
  fs.mkdirSync(path.dirname(outPath), { recursive: true });

  // Placeholder nav - will be fixed in second pass
  const wrapped = wrapHtml(title, body, null, null, "../index.html");
  fs.writeFileSync(outPath, wrapped, "utf8");

  fileIndex[f.relPath] = { title, htmlFile, dirName: f.dirName };
}

// Second pass: fix navigation links
// Build ordered list per directory
const dirFiles = {};
for (const f of allFiles) {
  const d = f.dirName || "(root)";
  if (!dirFiles[d]) dirFiles[d] = [];
  dirFiles[d].push(f);
}

for (const [dir, files] of Object.entries(dirFiles)) {
  for (let i = 0; i < files.length; i++) {
    const f = files[i];
    const info = fileIndex[f.relPath];
    info.prev = i > 0 ? fileIndex[files[i - 1].relPath].htmlFile : null;
    info.next = i < files.length - 1 ? fileIndex[files[i + 1].relPath].htmlFile : null;
  }
}

// Rewrite HTML with proper nav
console.log("Fixing navigation links...");
for (const f of allFiles) {
  const info = fileIndex[f.relPath];
  const raw = fs.readFileSync(f.fullPath, "utf8");
  const body = txtToHtmlBody(raw);
  const upLink = "../index.html";
  const prevLink = info.prev ? path.relative(path.dirname(info.htmlFile), info.prev).replace(/\\/g, "/") : null;
  const nextLink = info.next ? path.relative(path.dirname(info.htmlFile), info.next).replace(/\\/g, "/") : null;

  const wrapped = wrapHtml(info.title, body, prevLink, nextLink, upLink);
  const outPath = path.join(OUT_DIR, info.htmlFile);
  fs.writeFileSync(outPath, wrapped, "utf8");
}

// Generate index.html (TOC page)
console.log("Generating index.html...");
let tocHtml = `<h1>${CHM_TITLE}</h1>\n`;
tocHtml += '<p class="kv"><span class="key">文件总数：</span>' + allFiles.length + '</p>\n';
tocHtml += '<p class="kv"><span class="key">生成时间：</span>' + new Date().toLocaleString("zh-CN") + '</p>\n';
tocHtml += '<hr>\n';

// Build sorted directory tree
const sortedDirs = Object.keys(dirFiles).sort((a, b) => {
  if (a === "(root)") return -1;
  if (b === "(root)") return 1;
  return a.localeCompare(b, "zh-CN");
});

for (const dir of sortedDirs) {
  const files = dirFiles[dir].sort((a, b) => a.baseName.localeCompare(b.baseName, "zh-CN"));
  const displayDir = dir.replace(/\\/g, " / ");
  tocHtml += `<h2>${escapeHtml(displayDir)}</h2>\n<ul>\n`;
  for (const f of files) {
    const info = fileIndex[f.relPath];
    tocHtml += `  <li><a href="${info.htmlFile.replace(/\\/g, "/")}">${escapeHtml(info.title)}</a></li>\n`;
  }
  tocHtml += "</ul>\n";
}

const indexWrapped = wrapHtml(CHM_TITLE, tocHtml, null, null, null);
fs.writeFileSync(path.join(OUT_DIR, "index.html"), indexWrapped, "utf8");

// Generate HHP project file
console.log("Generating HHP project file...");
let hhpFiles = "";
for (const f of allFiles) {
  const info = fileIndex[f.relPath];
  hhpFiles += info.htmlFile.replace(/\\/g, "/") + "\n";
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

// Generate HHC contents file
console.log("Generating HHC contents file...");
let hhc = `<!DOCTYPE HTML PUBLIC "-//IETF//DTD HTML//EN">
<HTML>
<HEAD>
<meta name="GENERATOR" content="build_chm.mjs">
</HEAD>
<BODY>
<OBJECT type="text/site properties">
  <param name="Window Styles" value="0x800025">
</OBJECT>
<UL>
  <LI><OBJECT type="text/sitemap">
    <param name="Name" value="${CHM_TITLE}">
    <param name="Local" value="index.html">
  </OBJECT>
`;

for (const dir of sortedDirs) {
  const files = dirFiles[dir].sort((a, b) => a.baseName.localeCompare(b.baseName, "zh-CN"));
  const displayDir = dir.replace(/\\/g, " / ");
  hhc += `  <UL>\n    <LI><OBJECT type="text/sitemap">\n      <param name="Name" value="${escapeHtmlXml(displayDir)}">\n      <param name="Local" value="index.html#${encodeURIComponent(dir)}">\n    </OBJECT>\n`;
  for (const f of files) {
    const info = fileIndex[f.relPath];
    hhc += `    <LI><OBJECT type="text/sitemap">\n      <param name="Name" value="${escapeHtmlXml(info.title)}">\n      <param name="Local" value="${info.htmlFile.replace(/\\/g, "/")}">\n    </OBJECT>\n`;
  }
  hhc += "  </UL>\n";
}

hhc += `</UL>
</BODY>
</HTML>`;
fs.writeFileSync(path.join(OUT_DIR, "toc.hhc"), hhc, "utf8");

function escapeHtmlXml(str) {
  return str.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

console.log(`\nDone! Output: ${OUT_DIR}`);
console.log(`Files: ${allFiles.length} HTML + index.html + project.hhp + toc.hhc`);
console.log(`\nNext: Compile with hhc.exe`);
console.log(`  cd "${OUT_DIR}" && hhc.exe project.hhp`);
