# Philche — AI Agent Skill Security Scanner

> **A silent, diligent guardian for the age of AI spells.**  
> Philche continuously monitors AI agent skill files — offline, on your machine, without sending data anywhere.

![Philche banner](images/philche.png)

---

## Overview

`Philche` is a security daemon that resides in the system tray. It scans AI Agent's skill prompts (`.md`) and script files (`.js`, `.py`, etc.) offline on the local machine, detects malicious prompt word injection, data leakage intentions, and dangerous script behaviors, and instantly reminds you with Toast notifications.

**All analysis is performed on your computer and no data is sent to external servers. **

>The mantra of the AI era is the prompt word. The world does not lack colorful and outstanding magicians, but it lacks a boring but dedicated caretaker. Philche is that caretaker.

---

## Supported AI Agents

Philche can automatically explore the skill paths of the following Agent installations, including WSL environments:

| Agents                  | Skill paths                 |
|-------------------------|-----------------------------|
| GitHub Copilot CLI      | `~/.copilot/skills/`        |
| OpenAI Codex CLI        | `~/.codex/skills/`          |
| Google Gemini CLI       | `~/.gemini/skills/`         |
| Anthropic Claude Code   | `~/.claude/skills/`         |
| Anthropic Claude Cowork | `~/.cowork/skills/`         |
| OpenClaw                | Support integrated scanning |

---

## Features

### 🔍 Multi-layer scan engine

| Layers                            | Detection items                                                                                                                                                                                 |
|-----------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Keyword/Rule Detection**        | Malicious command phrases (`ignore previous instructions`, `jailbreak`, `exfiltrate`...), Unicode invisible characters (zero-width characters, BiDi control characters), dangerous Regex styles |
| **Code scanning (YARA style)**    | Suspicious shell execution, credential theft, encoded payload and other patterns in scripts                                                                                                     |
| **LLM intent classification**     | Use the **Llama Guard 3 8B**quantized model to infer skill intent natively, no GPU required                                                                                                     |
| **Semantic similarity detection** | Compare the semantic similarity of known malicious samples                                                                                                                                      |

### 🔔 Notifications and Scheduling

- **Periodic scan**: Customizable interval (default 60 minutes)
- **Real-time monitoring**: Automatically trigger scanning when files change
- **Incremental Scan**: Skip unchanged files to save resources
- **Toast Notification**: Instant pop-up reminder when a risk is discovered
- **Risk Level**: `Low`, `Medium`, `High`

### 🖥️ Other features

- Permanent system bar, right-click menu for quick operation
- Windows File Explorer right-click integration (directly triggers scanning of folders)
- Setting window: Findings list, Agent management, Model path, Language switching

---

## System requirements

| Environment         | Requirements                                                         |
|---------------------|----------------------------------------------------------------------|
| Operating system    | Windows 10/11 (64-bit)                                               |
| Execution Framework | [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| Memory              | 4 GB or more recommended (LLM scan mode requires about 3 GB)         |
| GPU                 | **Not required**(Pure CPU inference)                                 |
| Disk Space          | Approximately 5 GB (with Llama Guard model)                          |

---

## Installation

### 1. How to build

Confirm that [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) is installed, then execute:

```cmd
git clone https://github.com/tzengshinfu/Philche.git
cd Philche
dotnet build ./Philche/Philche.slnx
```

### 2. Download the LLM security model

Philche uses **Llama Guard 3 8B Q4_K_M**for native intent classification. After first boot:

1. Right-click on the system tray icon → **Settings**
2. Switch to the **Models**tab
3. Confirm that the model name is `Llama-Guard-3-8B-Q4_K_M-GGUF` and click **Download**
4. Wait for the download to complete (model approximately 4–5 GB, source: HuggingFace)

> You can also manually download the `.gguf` file and directly specify the local path in the model path field.

### 3. Run

```cmd
./Philche/Philche.Tray/bin/Debug/net10.0/Philche.Tray.exe
```

After startup, Philche will reside in the system tray (notification area on the right side of the taskbar).

---

## Usage

### System tray context menu

| Options              | Descriptions                                                               |
|----------------------|----------------------------------------------------------------------------|
| Scan now             | Perform a scan of all configured Agent skill paths                         |
| Settings             | Open the settings window (Agent management, model path, schedule settings) |
| Periodic scan        | Turn on/off scheduled scan                                                 |
| Real-time monitoring | Turn on/off automatic scanning of file changes                             |
| End                  | Exit program                                                               |

### Scan from File Explorer

Right-click on any folder and select "Scan with Philche" to perform an instant scan of the skill files in the folder.

### CLI one-time scan mode

Philche will start in GUI resident mode by default; if you want to do a one-time scan and end it in the command line, you can add `--cli`:

```cmd
:: Scan a single file
./Philche/Philche.Tray/bin/Debug/net10.0-windows/Philche.Tray.exe --cli --scan "C:\agents\SKILL.md"

:: Scan the entire directory
./Philche/Philche.Tray/bin/Debug/net10.0-windows/Philche.Tray.exe --cli --scan "C:\agents"

:: Output results as JSON
./Philche/Philche.Tray/bin/Debug/net10.0-windows/Philche.Tray.exe --cli --scan "C:\agents" --format json
```

- `--scan`: can access one or more files or directories.
- `--format text|json`: Output format, default is `text`.
- The directory will automatically expand recursively into scannable file types supported by Philche.

CLI exit code：
| Codes | Descriptions                                     |
|-------|--------------------------------------------------|
| `0`   | All scan results are `Low`                       |
| `1`   | At least one result is `Medium`                  |
| `2`   | At least one result is `High`                    |
| `3`   | Parameter error, target not found or scan failed |

### View scan results

When a risk is discovered, a Toast notification will pop up in the lower right corner of Windows.  
The complete list of Findings can be viewed on the main page of the settings window, including risk levels, triggering reasons and affected file paths.

---

## Configuration

Settings are stored in YAML format (the default path can be overridden via the environment variable `PHILCHE_SETTINGS_YAML_PATH`):

```yaml
version: 1
agents:
  - agentKey: github-copilot-cli
    displayName: GitHub Copilot CLI
    skillsPaths:
      - path: C:\Users\YourName\.copilot\skills
        trusted: false
models:
  guardModelPath: C:\path\to\llama-guard-3-8b-q4_k_m.gguf
scanning:
  enableMaliciousWordsScan: true
  enableInvisibleCharsScan: true
  enableLlmIntentScan: true
  enableYaraScan: true
  enableRegexScan: true
scheduler:
  periodicIntervalMinutes: 60
shell:
  contextMenuEnabled: true
```

The list of malicious phrases will be loaded from `malicious-phrases.txt` in the same directory as `settings.yaml`:

- One phrase per line
- Anything starting with `#` is treated as a comment
- If the file does not exist, Philche will automatically create a default list for subsequent direct editing

Example:

```text
# Prompt injection / jailbreak
ignore previous
jailbreak

# Chinese
忽略之前
越獄
```

Dangerous pattern Regex rules will be loaded from `dangerous-patterns.txt` in the same directory as `settings.yaml`:

- One Regex rule per line
- Anything starting with `#` is treated as a comment
- If the file does not exist, Philche will automatically create a default rule file
- Invalid Regex will be ignored and will not cause the overall scan to fail

Example:

```text
# Dangerous regex patterns
ignore\s+previous
api[_-]?key
credit\s*card
```

---

## Tests for Developers

```cmd
:: Execute unit tests (excluding integration tests that require an environment)
dotnet test ./Philche/Philche.Core.Test/Philche.Core.Test.csproj `
  --filter "FullyQualifiedName!~OpenClawScanIntegrationTests"

:: Execute OpenClaw integration tests
dotnet test ./Philche/Philche.Core.Test/Philche.Core.Test.csproj `
  --filter "FullyQualifiedName~OpenClawScanIntegrationTests"
```

---

## Disclaimer

Philche's scan results are for auxiliary judgment and do not constitute an absolute security guarantee. The LLM classification model may cause misjudgments (false positive /false negative). Please evaluate your own risks before installing or executing a skill.

---

## License

For third-party licensing information for this project, please refer to the [THIRD-PARTY-LICENSES/](THIRD-PARTY-LICENSES/) folder.
