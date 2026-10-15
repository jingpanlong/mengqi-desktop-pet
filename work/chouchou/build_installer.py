# -*- coding: utf-8 -*-
"""Build 萌栖安装版.exe (embeds 萌栖.exe as payload, mirrors 生成安装版.ps1)."""
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
os.chdir(ROOT)

windir = os.environ.get("WINDIR", r"C:\Windows")
compiler = os.path.join(windir, "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe")
if not os.path.exists(compiler):
    compiler = os.path.join(windir, "Microsoft.NET", "Framework", "v4.0.30319", "csc.exe")

arguments = [
    "/nologo", "/target:winexe", "/optimize+", "/out:萌栖安装版.exe",
    "/win32icon:src\\app.ico",
    "/win32manifest:src\\app.manifest",
    "/resource:萌栖.exe,mengqi.payload",
    "/reference:System.Windows.Forms.dll",
    "/reference:System.Drawing.dll",
    os.path.join("installer", "Setup.cs"),
]

proc = subprocess.run([compiler] + arguments, capture_output=True, text=True, errors="replace")
out = (proc.stdout or "") + (proc.stderr or "")
if out.strip():
    print(out)
print("exit:", proc.returncode)
sys.exit(proc.returncode)
