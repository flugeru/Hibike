"""Run: python tests/check_installer_reset.py (does not touch user data)."""
from pathlib import Path

source = (Path(__file__).resolve().parents[1] / "packaging/windows/hibike.iss").read_text(encoding="utf-8")
task = next(line for line in source.splitlines() if line.startswith('Name: "cleardata"'))
assert "Flags: unchecked" in task
assert "UsePreviousTasks=no" in source
reset = source[source.index("[Code]"):]
assert "MB_DEFBUTTON2" in reset
assert "if WizardSilent or not ClearDataConfirmed then Exit;" in reset
assert "{localappdata}\\Hibike-data-backups" in reset
assert "RenameFile(BackupDir + '\\Hibike', CurrentData)" in reset
assert not any(call in reset for call in ("DelTree(", "DeleteFile(", "RemoveDir("))
print("Installer reset checks passed: opt-in, confirmation, silent guard, backup and rollback; no deletion.")
