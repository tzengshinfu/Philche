# Philche — AI Agent Skill Security Scanner

> **A silent, diligent guardian for the age of AI spells.**  
> Philche continuously monitors AI agent skill files — offline, on your machine, without sending data anywhere.

![Philche banner](images/philche.png)

---

## 概述 Overview

`Philche`是一個常駐系統列的安全守護程式，在本機離線掃描 AI Agent 的 skill 提示詞（`.md`）與腳本檔案（`.js`、`.py` 等），偵測惡意提示詞注入、資料外洩意圖、危險腳本行為，並即時以 Toast 通知提醒你。

**一切分析皆在你的電腦上進行，不傳送任何資料至外部伺服器。**

> AI 時代的咒語是提示詞。這個世界不缺多采多姿的傑出魔法師，但缺少一個呆板無趣、卻盡職盡責的管理員。Philche 就是那個管理員。

---

## 支援的 AI Agent

Philche 可自動探索以下 Agent 安裝的 skill 路徑，包含 WSL 環境：

| Agent | 備註 |
|---|---|
| GitHub Copilot CLI | `~/.copilot/skills/` |
| OpenAI Codex CLI | `~/.codex/skills/` |
| Google Gemini CLI | `~/.gemini/skills/` |
| Anthropic Claude Code | `~/.claude/skills/` |
| Anthropic Claude Cowork | `~/.cowork/skills/` |
| OpenClaw | 支援整合掃描 |

---

## 功能特色 Features

### 🔍 多層掃描引擎

| 引擎 | 偵測項目 |
|---|---|
| **關鍵字 / 規則偵測** | 惡意指令詞組（`ignore previous instructions`、`jailbreak`、`exfiltrate`...）、Unicode 隱形字元（零寬字元、BiDi 控制符）、危險 Regex 樣式 |
| **程式碼掃描（YARA 風格）** | 腳本中的可疑 shell 執行、憑證竊取、編碼 payload 等模式 |
| **LLM 意圖分類** | 使用 **Llama Guard 3 8B** 量化模型在本機推論 skill 意圖，無需 GPU |
| **語意相似度偵測** | 比對已知惡意樣本的語意相似程度 |

### 🔔 通知與排程

- **定期掃描**：可自訂間隔（預設 60 分鐘）
- **即時監控**：檔案變更時自動觸發掃描
- **增量掃描**：跳過未變更檔案，節省資源
- **Toast 通知**：發現風險時即時彈出提醒
- **風險等級**：`低 Low` / `中 Medium` / `高 High`

### 🖥️ 其他特色

- 系統列常駐，右鍵選單快速操作
- Windows 檔案總管右鍵整合（對資料夾直接觸發掃描）
- 設定視窗：Findings 列表、Agent 管理、模型路徑、語言切換

---

## 系統需求 Requirements

| 項目 | 需求 |
|---|---|
| 作業系統 | Windows 10 / 11（64-bit） |
| 執行框架 | [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| 記憶體 | 建議 4 GB 以上（LLM 掃描模式需約 3 GB） |
| GPU | **不需要**（純 CPU 推論） |
| 磁碟空間 | 約 5 GB（含 Llama Guard 模型） |

---

## 安裝與設定 Installation

### 1. 建置

確認已安裝 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，然後執行：

```powershell
git clone https://github.com/your-org/Philche.git
cd Philche
dotnet build ./Philche/Philche.slnx
```

### 2. 下載 LLM 安全模型

Philche 使用 **Llama Guard 3 8B Q4_K_M** 進行本機意圖分類。首次啟動後：

1. 在系統列圖示上按右鍵 → **設定**
2. 切換到 **模型（Models）** 頁籤
3. 確認模型名稱為 `Llama-Guard-3-8B-Q4_K_M-GGUF`，點擊 **下載**
4. 等待下載完成（模型約 4–5 GB，來源：HuggingFace）

> 也可手動下載 `.gguf` 檔案後，在模型路徑欄位直接指定本機路徑。

### 3. 執行

```powershell
./Philche/Philche.Tray/bin/Debug/net10.0-windows/Philche.Tray.exe
```

啟動後 Philche 將常駐於系統列（工作列右側通知區域）。

---

## 使用方法 Usage

### 系統列右鍵選單

| 選項 | 說明 |
|---|---|
| 立即掃描 | 對所有已設定的 Agent skill 路徑執行一次掃描 |
| 設定 | 開啟設定視窗（Agent 管理、模型路徑、排程設定） |
| 定期掃描 | 開啟 / 關閉定時掃描 |
| 即時監控 | 開啟 / 關閉檔案變更自動掃描 |
| 結束 | 退出程式 |

### 從檔案總管掃描

在任意資料夾上按右鍵，選擇 **「使用 Philche 掃描」**，即可對該資料夾內的 skill 檔案執行即時掃描。

### CLI 一次性掃描模式

Philche 預設會以 GUI 常駐模式啟動；若要在命令列中做一次性掃描並結束，可加入 `--cli`：

```powershell
# 掃描單一檔案
./Philche/Philche.Tray/bin/Debug/net10.0-windows/Philche.Tray.exe --cli --scan "C:\agents\SKILL.md"

# 掃描整個目錄
./Philche/Philche.Tray/bin/Debug/net10.0-windows/Philche.Tray.exe --cli --scan "C:\agents"

# 以 JSON 輸出結果
./Philche/Philche.Tray/bin/Debug/net10.0-windows/Philche.Tray.exe --cli --scan "C:\agents" --format json
```

- `--scan`：可接一個或多個檔案或目錄。
- `--format text|json`：輸出格式，預設為 `text`。
- 目錄會自動遞迴展開為 Philche 支援的可掃描檔案類型。

CLI exit code：

| Code | 說明 |
|---|---|
| `0` | 所有掃描結果皆為 `Low` |
| `1` | 至少一個結果為 `Medium` |
| `2` | 至少一個結果為 `High` |
| `3` | 參數錯誤、找不到目標或掃描失敗 |

### 查看掃描結果

發現風險時，Windows 右下角會彈出 Toast 通知。  
完整的 Findings 清單可在設定視窗的主頁面查看，包含風險等級、觸發原因與受影響檔案路徑。

---

## 設定檔 Configuration

設定以 YAML 格式儲存（預設路徑可透過環境變數 `PHILCHE_SETTINGS_YAML_PATH` 覆蓋）：

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

惡意詞組清單會從與 `Settings.yaml` 同目錄的 `malicious-phrases.txt` 載入：

- 每行一個詞組
- `#` 開頭視為註解
- 若檔案不存在，Philche 會自動建立一份預設清單，供後續直接編輯

範例：

```text
# Prompt injection / jailbreak
ignore previous
jailbreak

# Chinese
忽略之前
越獄
```

危險樣式 Regex 規則會從與 `Settings.yaml` 同目錄的 `dangerous-patterns.txt` 載入：

- 每行一條 Regex 規則
- `#` 開頭視為註解
- 若檔案不存在，Philche 會自動建立一份預設規則檔
- 無效 Regex 會被忽略，不會讓整體掃描失敗

範例：

```text
# Dangerous regex patterns
ignore\s+previous
api[_-]?key
credit\s*card
```

---

## 開發者資訊 For Developers

```powershell
# 執行單元測試（排除需要環境的整合測試）
dotnet test ./Philche/Philche.Core.Test/Philche.Core.Test.csproj `
  --filter "FullyQualifiedName!~OpenClawScanIntegrationTests"

# 執行 OpenClaw 整合測試
dotnet test ./Philche/Philche.Core.Test/Philche.Core.Test.csproj `
  --filter "FullyQualifiedName~OpenClawScanIntegrationTests"
```

---

## 免責聲明 Disclaimer

Philche 的掃描結果為輔助判斷，不構成絕對的安全保證。LLM 分類模型可能出現誤判（false positive / false negative）。安裝或執行 skill 前，請自行評估風險。

---

## 授權 License

本專案的第三方授權資訊請參閱 [THIRD-PARTY-LICENSES/](THIRD-PARTY-LICENSES/) 資料夾。
