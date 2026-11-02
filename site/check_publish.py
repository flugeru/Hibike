"""Run after dotnet publish site/Hibike.Web.csproj -c Release -o web-publish."""
from pathlib import Path
import re
import json

root = Path(__file__).resolve().parent.parent
web = root / "web-publish/wwwroot"
html = (web / "index.html").read_text(encoding="utf-8")
script = re.search(r'src="(main[^"\s]*\.js)"', html)
assert script and (web / script[1]).is_file(), "Missing fingerprinted entry script"
imports = json.loads(re.search(r'<script type="importmap">(.*?)</script>', html, re.S)[1])["imports"]
assert (web / imports["./_framework/dotnet.js"]).is_file(), "Missing .NET runtime"
assert list((web / "_framework").glob("*.wasm")), "Missing WebAssembly assets"
for page in ["privacy.md", "terms.md", "googlef54a5400e34c1ee0.html"]:
    assert (root / "docs" / page).is_file(), f"Missing public document: {page}"
print("Browser publish checks passed: entry script, runtime, WebAssembly and legal documents.")
