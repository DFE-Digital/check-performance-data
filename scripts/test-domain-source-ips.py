#!/usr/bin/env python3
"""Test source policy expressions without Azure credentials or remote state."""
from pathlib import Path
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = root / "terraform/domains/environment_domains"
with tempfile.TemporaryDirectory(prefix="cpd-source-policy-") as directory:
    target = Path(directory)
    for name in ("variables.tf", "source_ip_rules.tf", "source_ip_validation.tf"):
        shutil.copy2(source / name, target / name)
    shutil.copytree(source / "tests", target / "tests")
    subprocess.run(["terraform", f"-chdir={target}", "init", "-backend=false"], check=True)
    subprocess.run(["terraform", f"-chdir={target}", "test"], check=True)
