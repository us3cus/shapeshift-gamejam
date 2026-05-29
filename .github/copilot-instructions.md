# Copilot 自定義指令

> 📌 此檔案為 VS Code GitHub Copilot Agent Mode 與 Claude Code 的統一指引。

---

## 專案概述

**MCP Server — Asset-Aware Medical RAG**

| 項目 | 說明 |
|------|------|
| 語言 | Python 3.10+ |
| 框架 | FastMCP, LightRAG, PyMuPDF; Marker is temporarily on security hold |
| 策略 | 雙引擎 PDF 解析（PyMuPDF 快速 + Marker 高精度） |

### 核心功能

- 📄 **PDF ETL** — 雙引擎文件拆解（圖片、表格、章節）
- 🧩 **Segmentation Export** — 統一 segmentation schema（reading order + line span）
- 📊 **A2T** — Anything to Table 表格建立
- 🧭 **Section Navigation** — 動態層級章節導航（5 Tools）
- 🔍 **Knowledge Graph** — 跨文獻知識圖譜（LightRAG）
- 🖼️ **Vision AI** — 圖片分析（base64 返回）
- 🔤 **OCR Preprocessing** — 掃描 PDF 按需前處理

### LLM 後端

- **預設**: Ollama (本地) — CPU `granite4.1:3b`；GPU hint `granite4.1:8b`；`nomic-embed-text` 僅在啟用 LightRAG/KG 時需要
- **備選**: OpenAI (需 API Key)

---

## 開發哲學 💡

> **「想要寫文件的時候，就更新 Memory Bank 吧！」**
>
> **「想要零散測試的時候，就寫測試檔案進 tests/ 資料夾吧！」**

- 不要另開檔案寫筆記，直接寫進 Memory Bank
- 今天的零散測試，就是明天的回歸測試

---

## 法規遵循

你必須遵守以下法規層級：

1. **憲法**：`CONSTITUTION.md` - 最高原則，不可違反
2. **子法**：`.github/bylaws/*.md` - 細則規範
3. **技能**：`.claude/skills/*/SKILL.md` - 操作程序

---

## 架構原則

- 採用 **DDD (Domain-Driven Design)**
- **DAL (Data Access Layer) 必須獨立**
- 依賴方向：`Presentation → Application → Domain ← Infrastructure`
- 參見子法：`.github/bylaws/ddd-architecture.md`

---

## Python 環境（uv 優先）

- 新專案必須使用 uv 管理套件
- 必須建立虛擬環境（禁止全域安裝）
- 參見子法：`.github/bylaws/python-environment.md`

```bash
# 初始化環境
uv venv
uv sync                    # 基本安裝
# v0.6.27: Marker extra is temporarily disabled because marker-pdf pins Pillow<11.

# 安裝依賴
uv add package-name
uv add --dev pytest ruff
```

---

## Memory Bank 同步

每次重要操作必須更新 Memory Bank：

| 操作 | 更新文件 |
|------|----------|
| 完成任務 | `progress.md` (Done) |
| 開始任務 | `progress.md` (Doing), `activeContext.md` |
| 重大決策 | `decisionLog.md` |
| 架構變更 | `architect.md` |

參見子法：`.github/bylaws/memory-bank.md`

---

## Git 工作流

提交前必須執行檢查清單：

1. ✅ Memory Bank 同步（必要）
2. 📖 README 更新（如需要）
3. 📋 CHANGELOG 更新（如需要）
4. 🗺️ ROADMAP 標記（如需要）

**⚠️ PUSH 後必須檢查 CI 狀態！**
- 推送完成後立即檢查 GitHub Actions 是否通過
- 如有失敗，優先修復 CI 問題再繼續開發

**📊 更新工具數量時必須使用 Script**
- 執行 `./scripts/count_tools.sh` (Linux/macOS) or `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/count_tools.ps1` (Windows) 統計實際數量
- 不可手動計算或猜測工具數量
- 確保 README、copilot-instructions、VSCode extension README 三處同步

參見子法：`.github/bylaws/git-workflow.md`

---

## VSIX / MCP Harness 同步

VS Code extension 發布前必須確認 assistant harness 和 MCP 設定路徑同步：

- VSIX 會打包 `vscode-extension/resources/repo-assets/asset-aware/**`
- 安裝/啟動時會同步 workspace harness：`AGENTS.md`、`.github/copilot-instructions.md`、`.github/agents/asset-aware-document.agent.md`、`.cline/skills/asset-aware-mcp-harness`、`.codex/skills/asset-aware-mcp-harness`、`.clinerules`
- MCP 設定會保守 merge：Copilot `.vscode/mcp.json`、Cline `cline_mcp_settings.json`、Codex `~/.codex/config.toml`
- 不可覆蓋使用者自訂 server、Codex comments、Cline `alwaysAllow` 或 unrelated MCP entries

必要檢查：

```bash
cd vscode-extension
npm run sync-assets:check
npm run test:ci
```

---

## 可用 Skills

位於 `.claude/skills/` 目錄：

| Skill | 功能 | 觸發詞 |
|-------|------|--------|
| git-precommit | Git 提交前編排器 | `GIT`, `commit`, `push` |
| git-doc-updater | Git 提交前文檔同步 | `docs`, `文檔`, `sync docs` |
| ddd-architect | DDD 架構輔助 | `DDD`, `arch`, `新功能` |
| code-refactor | 主動重構與模組化 | `RF`, `refactor`, `重構` |
| memory-updater | Memory Bank 同步 | `MB`, `memory`, `記憶` |
| memory-checkpoint | 記憶檢查點 | `CP`, `checkpoint`, `存檔` |
| readme-updater | README 智能更新 | `readme`, `說明` |
| readme-i18n | README 多語言同步 | `i18n`, `翻譯`, `translate` |
| changelog-updater | CHANGELOG 自動更新 | `CL`, `changelog` |
| roadmap-updater | ROADMAP 狀態追蹤 | `RM`, `roadmap` |
| code-reviewer | 程式碼審查 | `CR`, `review`, `審查` |
| test-generator | 測試生成 | `TG`, `test`, `測試` |
| project-init | 專案初始化 | `init`, `new`, `新專案` |
| **pdf-asset-extractor** | **PDF→圖文分解+知識圖譜** | `PDF`, `ingest`, `figure`, `table`, `知識圖譜` |

> ⚠️ **pdf-asset-extractor 注意**：圖片 base64 非常大，一次只處理一張！

---

## 💸 Memory Checkpoint 規則

為避免對話被 Summarize 壓縮時遺失重要上下文：

### 主動觸發時機

1. 對話超過 **10 輪**
2. 累積修改超過 **5 個檔案**
3. 完成一個 **重要功能/修復**
4. 使用者說要 **離開/等等**

### 執行指令

- 「記憶檢查點」「checkpoint」「存檔」
- 「保存記憶」「sync memory」

### 必須記錄

- 當前工作焦點
- 變更的檔案列表（完整路徑）
- 待解決事項
- 下一步計畫

---

## 回應風格

- 使用**繁體中文**
- 提供清晰的步驟說明
- 引用相關法規條文
- 執行操作後更新 Memory Bank

---

## 目錄結構約定

```
src/
├── domain/           # 核心領域（純業務邏輯，無外部依賴）
│   ├── entities.py        # Document, Asset, Section 等核心實體
│   ├── table_entities.py  # A2T 表格相關實體
│   ├── section_tree.py    # SectionTree 章節樹結構
│   ├── chunking.py        # 文本分塊策略
│   └── repositories.py    # Repository 介面定義
├── application/      # 應用層（用例編排）
│   ├── document_service.py  # ETL 文件處理（雙引擎）
│   ├── table_service.py     # A2T 表格服務
│   ├── section_service.py   # 章節導航服務
│   ├── asset_service.py     # 資產查詢服務
│   ├── knowledge_service.py # 知識圖譜服務
│   └── job_service.py       # 非同步工作管理
├── infrastructure/   # 基礎設施（DAL、外部服務）
│   ├── file_storage.py      # 檔案儲存 Repository 實作
│   ├── pdf_extractor.py     # PyMuPDF 快速提取
│   ├── marker_adapter.py    # Marker 高精度提取
│   ├── excel_renderer.py    # Excel 渲染
│   ├── lightrag_adapter.py  # LightRAG 知識圖譜
│   └── config.py            # 配置管理
└── presentation/     # 呈現層（MCP Server, 模組化）
    ├── server.py            # Thin entry point (31 行)
    ├── mcp_app.py           # FastMCP 單一實例
    ├── dependencies.py      # Composition Root
    ├── tools/               # 59 tools (7 模組)
    │   ├── document_tools.py   # ETL + document management (18)
    │   ├── docx_tools.py       # Docx ↔ DFM + conversion (16)
    │   ├── section_tools.py    # Navigation (5)
    │   ├── job_tools.py        # Job management (4)
    │   ├── knowledge_tools.py  # KG (3)
    │   ├── profile_tools.py    # Profile (6)
    │   └── table_tools.py      # A2T (7) — operation-based
    └── resources/           # 13 resources (2 模組)
        ├── document_resources.py  # Documents (8)
        └── table_resources.py     # Tables (5)
```

---

## 重要檔案參考

| 檔案 | 用途 |
|------|------|
| `CONSTITUTION.md` | 專案憲法（最高原則） |
| `memory-bank/` | 專案記憶庫 |
| `docs/spec.md` | 技術規格 |
| `docs/marker-etl-spec.md` | Marker ETL 規格書 |
| `.github/bylaws/*.md` | 子法細則 |
| `.claude/skills/*/SKILL.md` | 技能操作程序 |
