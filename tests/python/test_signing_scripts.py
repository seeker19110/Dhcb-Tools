"""Exercise supplied-PFX imports without signing files or changing certificate stores."""

import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
POWERSHELL = shutil.which("powershell" if os.name == "nt" else "pwsh")
OPENSSL = shutil.which("openssl")
if not OPENSSL and os.name == "nt":
    git_openssl = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Git/usr/bin/openssl.exe"
    if git_openssl.is_file():
        OPENSSL = str(git_openssl)


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


@unittest.skipUnless(POWERSHELL and OPENSSL, "Requires PowerShell and OpenSSL for a temporary PFX")
class SigningScriptTests(unittest.TestCase):
    def test_supplied_password_imports_pfx_on_windows_powershell_51(self):
        source = (ROOT / "scripts/sign-addin.ps1").read_text(encoding="utf-8-sig")
        # Execute the actual supplied-PFX branch, before signing or certificate-store access.
        branch = re.search(r"(?ms)^if \(\$PfxPath\) \{.*?^\}(?=\nelse)", source).group()
        with tempfile.TemporaryDirectory(prefix="dhcb-signing-") as tmp:
            folder = Path(tmp)
            certificate = folder / "cert.pem"
            key = folder / "key.pem"
            pfx = folder / "[certificate].pfx"
            config = folder / "openssl.cnf"
            config.write_text("[req]\ndistinguished_name=dn\n[dn]\n", encoding="utf-8")
            env = {**os.environ, "OPENSSL_CONF": str(config)}
            subprocess.run([OPENSSL, "req", "-x509", "-newkey", "rsa:2048", "-nodes",
                            "-keyout", str(key), "-out", str(certificate), "-days", "1",
                            "-subj", "/CN=DHCB Signing Compatibility Test"],
                           check=True, capture_output=True, timeout=30, env=env)
            # Algorithms understood by both .NET Framework 4.8 and current .NET.
            subprocess.run([OPENSSL, "pkcs12", "-export", "-inkey", str(key), "-in", str(certificate),
                            "-out", str(pfx), "-passout", "pass:compat-test", "-keypbe", "PBE-SHA1-3DES",
                            "-certpbe", "PBE-SHA1-3DES", "-macalg", "sha1"],
                           check=True, capture_output=True, timeout=30, env=env)
            for path, expected_success in ((pfx, True), (certificate, False)):
                with self.subTest(private_key=expected_success):
                    probe = folder / "probe.ps1"
                    probe.write_text("$ErrorActionPreference = 'Stop'\n"
                                     "[Console]::OutputEncoding = [Text.Encoding]::UTF8\n"
                                     + "$PfxPath = " + quote(path) + "\n"
                                     + "$PfxPassword = ConvertTo-SecureString 'compat-test' -AsPlainText -Force\n"
                                     + "try {\n" + branch + "\n"
                                     + "if (-not $cert.HasPrivateKey -or $cert.Subject -ne 'CN=DHCB Signing Compatibility Test') { throw 'Wrong certificate' }\n"
                                     + "Write-Output 'PFX_IMPORT_OK'\n"
                                     + "} finally { if ($cert) { $cert.Dispose() } }\n", encoding="utf-8-sig")
                    result = subprocess.run([POWERSHELL, "-NoProfile", "-File", str(probe)],
                                            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=30)
                    self.assertEqual(expected_success, result.returncode == 0, result.stdout + result.stderr)
                    self.assertEqual(expected_success, "PFX_IMPORT_OK" in result.stdout)
                    if not expected_success:
                        self.assertIn("khong chua khoa rieng", result.stderr)


if __name__ == "__main__":
    unittest.main()
