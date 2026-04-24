# PowerShell.MCP vs ripple+pwsh: 比較評価と改善候補

作成日: 2026-04-24
評価者: Claude (opus-4-7, 1M context session)
背景: 同一セッション内で両 MCP server を実使用 (build loop + refactor 実装 + live テスト) し比較

本メモは忖度なしの評価。どちらの server を美化する意図もなく、実使用から観測した事実と improvement candidate を淡々と記録する。

---

## 範囲

「pwsh (PowerShell) を実行させる用途」に限定した比較。
`Build.ps1` の self-kill 問題、mcp-sitter の auto-respawn、shared visible console の人+AI 共通台哲学など、session 固有・外部依存要素は除外。

---

## PowerShell.MCP (pwsh MCP) が勝つ領域

### 1. 単発コマンドの応答速度
`"hello"` レベルで 0.3s 前後、ripple+pwsh の 0.8–1.1s より有意に速い。
ripple の prompt fn (OSC emission + exit-code resolver + $Error delta + ...) のチェーンが warm-up コストを生む。pwsh MCP はチェーンが薄い分だけ軽い。

### 2. 出力 envelope の安定性
status line が常にフル形式: `| Errors: 0 | Warnings: 0 | Info: 0 | Location [FileSystem]: ...`。
AI parser は条件分岐不要で同じ shape を期待できる。ripple は state 次第で `LastExit: N` / `--- errors ---` の有無が変わるので parser に条件分岐が増える。

### 3. シンプルさ (implementation / mental model)
pwsh の Error / Warning / Information / Verbose stream を素直にそのまま返す薄いラッパー。
ripple の $LASTEXITCODE snapshot や OSC R/T の machinery は「気が利いている」代償として複雑度が高い。

---

## ripple+pwsh が勝つ領域

### 1. 意味的な誠実さ (semantic fidelity)
`cmd /c exit 7; "after"` のようなパイプラインで、
- pwsh MCP: "成功" としか報告しない。native の非ゼロ exit は AI に完全不可視。
- ripple: `LastExit: 7` tag で surface する。

これは「取れない情報」ではなく「取ろうとしていない情報」の差。構造的なギャップ。

### 2. $LASTEXITCODE leak 防止
pwsh の $LASTEXITCODE は native exe のみ更新される global 変数。`cmd /c exit 7` 直後に `2+3` を走らせると、何もしていないのに exit 7 が居残る。
- pwsh MCP: 残り続ける値をそのまま報告する可能性あり。
- ripple: PreCommandLookupAction で snapshot を取り、post-command 値と比較、this-pipeline-changed-it 判定で leak を防ぐ。

### 3. Multi-line delivery の速度
`encoded_scriptblock` (base64 1-line invocation, dot-source 相当) で disk I/O ゼロ。
- pwsh MCP: multi-line 0.4s
- ripple: 0.1s (約 4×)

複雑な if / foreach / function 定義で顕著。

### 4. Cmdlet provenance の保持
`Get-Item /nope` のエラーが `Get-Item: Cannot find path '...'` (cmdlet prefix 付き) で返る。PowerShell ConciseView inline 表示と一致。
- pwsh MCP: `=== ERRORS ===` section に message だけ、cmdlet 名欠落。

### 5. 出力の選択権 (`strip_ansi`)
per-call で ANSI strip / retain を選べる。
- pwsh MCP: 一律 strip。色を見たいケースでは pwsh MCP を使えない。

---

## 互角 / トレードオフ

### Error 構造化
- pwsh MCP: `=== ERRORS === / === WARNINGS === / === INFO ===` 事前分離、直感的
- ripple: `--- errors (N) ---` 構造化リスト、Warnings/Info は trackしない

**トレードオフ**: pwsh MCP のほうが情報量多いが、`Write-Warning` は cmdlet 経由だと user proxy を迂回する — pwsh MCP の count が常に正確とは限らない (cmdlet 実装依存)。ripple はこれを理由に意識的に tracking しない。

### SGR preservation
- pwsh MCP: 一律 strip、token 安い
- ripple: 一律 preserve (default)、token 重い、`strip_ansi: true` で opt-in 制御可

### 出力 spill
- pwsh MCP: 30000 文字で temp file + `Show-TextFiles -Pattern` 誘導
- ripple: 15000 文字で spill + `search_files` / `read_file` 誘導

閾値と道具が違うだけで思想は同じ (overflow → external tool で drill-down)。

---

## 結論

**pwsh を実行するための道具として見ると:**

- **pwsh MCP は "早い・軽い・シンプル"**。PowerShell のネイティブ semantics をそのまま返す薄い API wrapper。
- **ripple+pwsh は "深い・遅い・賢い"**。pwsh の落とし穴 ($LASTEXITCODE leak, native exit を pipeline ok が隠す, cmdlet proxy の不透明さ) に積極的に補正をかける。

単発の「動かして結果ちょうだい」なら pwsh MCP で足りる。複雑なビルド / CI / スクリプト診断で pwsh の挙動を正確に把握したいなら ripple。

**エンジニアリング深度で言えば**: pwsh native semantics を忠実に反映 AND 落とし穴を補正する ripple が上。pwsh MCP は「PowerShell を素直に見せる」シンプルさが強みだが、「素直すぎて pwsh 自身の罠を素通しする」のが弱点。

AI を「秘書として速く動かす」なら pwsh MCP、「診断医として正確に現状を捉える」なら ripple。

---

# 改善候補

## pwsh MCP の改善候補

### 🔴 重要度 HIGH

#### 1. `SCOPE WARNING: Use $script:` の per-call 騒音

**現状**: `$r = ...` のような local variable assignment のたびに `⚠️ SCOPE WARNING: Local variable assignment(s) detected: $r → Consider using $script:r to preserve across calls` が出力される。1 session で 20+ 回見かけた。

**問題**:
- 警告そのものは有益 (pwsh MCP の session 間で local 変数は消える)
- でも出現頻度が過剰、1 度学べば覚えておける内容を毎回再教育
- 視覚ノイズが多い

**改善案**:
- session 開始時に 1 回 banner で教える
- または「同じ変数名で N 回警告したら以降は silent」のデチューン
- または verbose mode flag で opt-in

**実装コスト**: 小 (emission logic 1 箇所修正)

---

#### 2. Native exe の非ゼロ exit が silent

**現状**: `cmd /c exit 7; "after"` を実行すると pwsh MCP は「成功」としか報告しない。AI は cmd が exit 7 を返したことに気付けない。

**問題**: CI / build スクリプト診断で「native tool が警告的な非ゼロを返した」のを見逃す。
実際: `npm install` → `npm audit` が非ゼロで終わっても `&& 次のコマンド` で overall success にしてしまうと pwsh MCP は "OK" と報告する。

**改善案**: ripple の実装手法が移植可能:
1. PreCommandLookupAction で `$global:__pm_lec_at_start = $LASTEXITCODE` snapshot
2. prompt fn で `$lecChanged = $lec -ne $lecAtStart`
3. `$? true && $lecChanged && $lec != 0` のとき status line に `| NativeExit: N` 追加

**実装コスト**: 中 (prompt hook 相当に追加ロジック、integration script があれば ~40 行)

---

### 🟡 重要度 MEDIUM

#### 3. Status line の zero-tag 常時表示

**現状**: `| Errors: 0 | Warnings: 0 | Info: 0 |` を常に出力。happy path でも。

**問題**: token 浪費。ripple は N > 0 のときだけ出す、happy path では clean。

**改善案**: ゼロ値の tag を omit する (N > 0 時のみ render)。

**トレードオフ**: AI parser が「tag 不在 = 0」を前提にする必要あり。envelope shape の consistency が若干崩れる。今まで明示していた値が消えるので backwards compat に注意。

**実装コスト**: 小 (FormatStatusLine 相当で条件分岐)

---

#### 4. `Location [FileSystem]: ...` の provider prefix 冗長

**現状**: `Location [FileSystem]: C:\MyProj\ripple` のように provider 名明示。

**問題**: 99% のケースが `FileSystem`、冗長。他 provider (Env:, HKCU:, Variable:) は rare。

**改善案**: FileSystem のときは prefix 省略、非 FileSystem のときのみ `[Env]:` など付与。

**実装コスト**: 小

---

### 🟢 重要度 LOW

#### 5. First command latency

**現状**: session 最初の command が 1.11s、2 個目以降 0.3s 前後。

**問題**: session start 直後の体感の悪さ。

**改善案**: PowerShell.MCP module の init を lazy 化、または background pre-warm。原因分析必要。

**実装コスト**: 要 profiling

---

#### 6. `Show-TextFiles` integration の学習曲線

**現状**: 大きい出力は temp file に保存され、`Show-TextFiles -Pattern` cmdlet を使うよう促される。独自 cmdlet なので AI は初見では気付きにくい。

**改善案**: 標準 `Get-Content -Raw | Select-String` fallback の hint も併記、または prompt 側の tool description を強化。

**実装コスト**: 小 (prompt 文言の変更)

---

## ripple 側の改善候補 (参考)

### 🔴 重要度 HIGH

#### A. $LASTEXITCODE leak 検知の session-state 非決定性

`cmd /c exit 7; "after"` で LastExit が出たり出なかったり。直前に `$LASTEXITCODE = 7` の状態だと `$lecChanged = False` で検知漏れ。

完全修正は PS runtime 制約で困難。現状は docstring で制限を明示するのが最短。

### 🟡 重要度 MEDIUM

#### B. plain-text 大量出力の renderer 速度
2000 ファイル `Get-ChildItem` で 2.2s vs pwsh MCP 0.85s。未検証だが renderer の per-char cell allocation が疑わしい。deterministic な fast-path (run-based) を検討。

#### C. `HandleExecuteAsync` 504 行のメソッド分解
async/TCS/lock の coordination なので touch 時の risk 高。次に機能追加で触るタイミングで phased に。

### 🟢 重要度 LOW

#### D. Warning / Info stream の deterministic tracking
pwsh の `WARNING: ` / `INFORMATION: ` prefix は deterministic な契約であって heuristic ではない。単純に line prefix で数えれば pwsh MCP と同程度の精度を得られる。

#### E. SGR default の schema 化
shared-console 哲学で default preserve は合理的だが、AI-only worker pattern では token 浪費。console の visible 属性で default 切替の余地。

---

## 作業の着手順 (pwsh MCP 改善)

このメモの後に着手する順:

1. **#1 SCOPE WARNING のデチューン** — 最大 UX 改善 / 最小リスク。per-call → session-banner。
2. **#2 Native exe 非ゼロ exit の surface** — ripple の手法移植、CI 診断用途で実用的ゲイン大。
3. **#3 Zero-tag omission** — 実装小、backwards compat 要配慮。
4. **#4 Location provider prefix** — cosmetic、余力あれば。

#5, #6 は profiling / prompt wording の別作業、今回のセッションスコープ外。
