# -*- coding: utf-8 -*-
"""Build 萌栖.exe using the system .NET Framework compiler (mirrors 重新编译.ps1)."""
import glob
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))  # work/chouchou/build.py -> project root
os.chdir(ROOT)

windir = os.environ.get("WINDIR", r"C:\Windows")
compiler = os.path.join(windir, "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe")
if not os.path.exists(compiler):
    compiler = os.path.join(windir, "Microsoft.NET", "Framework", "v4.0.30319", "csc.exe")
if not os.path.exists(compiler):
    print("compiler not found")
    sys.exit(2)

speech_dir = os.path.join(windir, "Microsoft.NET", "assembly", "GAC_MSIL", "System.Speech")
speech = None
for dirpath, _dirs, files in os.walk(speech_dir):
    for name in files:
        if name == "System.Speech.dll":
            speech = os.path.join(dirpath, name)
            break
if not speech:
    print("System.Speech not found")
    sys.exit(2)

arguments = [
    "/nologo", "/target:winexe", "/optimize+", "/platform:anycpu", "/out:萌栖.exe",
    "/win32icon:src\\app.ico",
    "/win32manifest:src\\app.manifest",
    "/resource:src\\app.ico,mengqi.icon.ico",
    "/resource:src\\spritesheet.png,xiaohei.atlas.png",
    "/resource:src\\extra.png,mengqi.extra.png",
    "/reference:System.Windows.Forms.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Web.Extensions.dll",
    "/reference:System.Net.Http.dll",
    "/reference:System.Data.dll",
    "/reference:System.Security.dll",
    "/reference:" + speech,
    "/reference:System.IO.Compression.dll",
    "/reference:System.IO.Compression.FileSystem.dll",
]
arguments += sorted(glob.glob(os.path.join("src", "*.cs")))

def find_gac(name):
    hits = glob.glob(os.path.join(windir, "Microsoft.NET", "assembly", "GAC_MSIL", name, "v4.0_*", name + ".dll"))
    return sorted(hits)[-1] if hits else None

for assembly in ("UIAutomationClient", "UIAutomationTypes"):
    path = find_gac(assembly)
    if not path:
        print("missing GAC assembly:", assembly)
        sys.exit(2)
    arguments.append("/reference:" + path)

arguments += [
    "/resource:src\\chouchou.png,chouchou.atlas.png",
    "/resource:src\\chouchou-extra.png,chouchou.extra.png",
    "/resource:src\\app-icon.png,mengqi.icon.png",
]

proc = subprocess.run([compiler] + arguments, capture_output=True, text=True, errors="replace")
out = (proc.stdout or "") + (proc.stderr or "")
if out.strip():
    print(out)
print("exit:", proc.returncode)
sys.exit(proc.returncode)
