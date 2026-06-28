# 天章设计文档全集 CHM 构建脚本
# 用法: .\build_chm.ps1
$ErrorActionPreference = "Stop"
$root = "$PSScriptRoot"

Write-Host "=== 1/3 生成 HTML (ASCII安全文件名) ===" -ForegroundColor Cyan
Push-Location $root
node "$root\build_chm_v3.mjs"
if ($LASTEXITCODE -ne 0) { throw "HTML generation failed" }

Write-Host "`n=== 2/3 重命名 & GBK编码转换 ===" -ForegroundColor Cyan
node -e "
const fs=require('fs');const path=require('path');
const d=path.join('$root','html');
// rename
const allHtml=[];function walk(dir){for(const e of fs.readdirSync(dir,{withFileTypes:true})){const fp=path.join(dir,e.name);if(e.isDirectory())walk(fp);else if(e.name.endsWith('.html')&&e.name!=='index.html')allHtml.push({relPath:path.relative(d,fp)});}}
walk(d);allHtml.sort((a,b)=>a.relPath.localeCompare(b.relPath,'zh-CN'));
const mapping={};allHtml.forEach((f,i)=>{mapping[f.relPath]='f'+String(i+1).padStart(4,'0')+'.html';});
const reps=[];for(const[o,n]of Object.entries(mapping)){reps.push({old:o,new:n});const f=o.replace(/\\/g,'/');if(f!==o)reps.push({old:f,new:n});}
reps.sort((a,b)=>b.old.length-a.old.length);
function rpl(s){let r=s;for(const x of reps)r=r.split(x.old).join(x.new);return r;}
['index.html','toc.hhc','project.hhp'].forEach(fn=>{const fp=path.join(d,fn);fs.writeFileSync(fp,rpl(fs.readFileSync(fp,'utf8')),'utf8');});
for(const[o,n]of Object.entries(mapping)){const op=path.join(d,o),np=path.join(d,n);if(fs.existsSync(op)){fs.copyFileSync(op,np);fs.unlinkSync(op);}}
for(const e of fs.readdirSync(d,{withFileTypes:true}).filter(x=>x.isDirectory())){fs.rmSync(path.join(d,e.name),{recursive:true,force:true});}
let hhp=fs.readFileSync(path.join(d,'project.hhp'),'utf8');hhp=hhp.replace('Compiled file=../','Compiled file=../tianzhang_design.chm');hhp=hhp.replace('Compiled file=../tianzhang_design.chm天章设计文档全集.chm','Compiled file=../tianzhang_design.chm');fs.writeFileSync(path.join(d,'project.hhp'),hhp,'utf8');
console.log('Renamed '+allHtml.length+' files');
"
if ($LASTEXITCODE -ne 0) { throw "Rename failed" }

Write-Host "Converting to GBK..." -ForegroundColor Cyan
python "$root\_to_gbk.py"
if ($LASTEXITCODE -ne 0) { throw "GBK conversion failed" }

Write-Host "`n=== 3/3 编译 CHM ===" -ForegroundColor Cyan
subst T: "$root"
try {
    Push-Location "T:\html"
    & "T:\hhw\hhc.exe" "project.hhp"
    Pop-Location
    if ($LASTEXITCODE -ne 0) { throw "HHC compilation failed" }
    Remove-Item "T:\天章设计文档全集.chm" -Force -ErrorAction SilentlyContinue
    Rename-Item "T:\tianzhang_design.chm" "天章设计文档全集.chm"
} finally {
    subst T: /d
}

$chm = Get-Item "$root\天章设计文档全集.chm"
Write-Host "`n=== 完成 ===" -ForegroundColor Green
Write-Host "  $($chm.Name) — $([math]::Round($chm.Length/1KB,1)) KB — $($chm.LastWriteTime)" -ForegroundColor Green
Pop-Location
