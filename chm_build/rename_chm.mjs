// Rename compiled CHM to final Chinese filename
// Called from build_chm.ps1 step 3 to avoid PowerShell Unicode encoding issues
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const src = path.join(__dirname, "tianzhang_design.chm");
const dst = path.join(__dirname, "天章设计文档全集.chm");

if (!fs.existsSync(src)) {
    console.error("Source not found:", src);
    process.exit(1);
}

// Try to delete existing destination (may be locked)
try { fs.unlinkSync(dst); } catch (e) { /* ok if not exists */ }

fs.renameSync(src, dst);
const stat = fs.statSync(dst);
console.log("OK " + (stat.size / 1024).toFixed(1) + " KB");
