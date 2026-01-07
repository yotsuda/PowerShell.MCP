# PowerShell.MCP 複数コンソール操作テストケース

## 凡例
- 🤖 Claude が実行
- 👤 ユーザーが操作
- ✅ 期待結果

---

## 1. 基本動作確認

### 1.1 単一コンソールの起動と実行
🤖 `invoke_expression('Write-Output "Hello"')`
✅ 新しいコンソールが起動し、"Hello" が返る

### 1.2 コンソールの継続利用
🤖 `invoke_expression('$x = 123; Write-Output $x')`
✅ 同じコンソールで実行され、"123" が返る

---

## 2. 長時間コマンドとキャッシュ

### 2.1 タイムアウトとキャッシュ回収
🤖 `invoke_expression('Start-Sleep -Seconds 200; Write-Output "Completed"')`
✅ 約3分後に "Command is still running..." メッセージ

🤖 `wait_for_completion(timeout_seconds=120)`
✅ コマンド完了後、"Completed" を含むキャッシュが返る

### 2.2 停止ボタン + wait_for_completion
🤖 `invoke_expression('Start-Sleep -Seconds 60; Write-Output "Done at $(Get-Date)"')`
👤 約10秒後に停止ボタンを押す

🤖 `wait_for_completion(timeout_seconds=90)`
✅ コマンド完了後、結果が返る（"Done at ..." を含む）

### 2.3 wait_for_completion の早期リターン
🤖 `invoke_expression('Start-Sleep -Seconds 10; Write-Output "Quick"')`
👤 すぐに停止ボタンを押す

🤖 `wait_for_completion(timeout_seconds=60)`
✅ 10秒程度で結果が返る（60秒待たない）

### 2.4 busy コンソールがない場合
🤖 `wait_for_completion(timeout_seconds=30)`
✅ 即座に "No busy consoles to wait for." が返る

---

## 3. 複数コンソールの並行操作

### 3.1 並行実行と別コンソールでの操作
🤖 `invoke_expression('Start-Sleep -Seconds 60; Write-Output "Console1"')`
👤 約10秒後に停止ボタンを押す

🤖 `invoke_expression('Write-Output "Console2"')`
✅ 新しいコンソールが起動し、"Console2" が返る
✅ レスポンスに Console1 の busy ステータスが含まれる

🤖 `wait_for_completion(timeout_seconds=90)`
✅ Console1 の結果 "Console1" が返る

### 3.2 複数の busy コンソール
🤖 コンソール1: `invoke_expression('Start-Sleep -Seconds 60; Write-Output "A"')`
👤 停止ボタンを押す

🤖 コンソール2で: `invoke_expression('Start-Sleep -Seconds 30; Write-Output "B"')`
👤 停止ボタンを押す

🤖 `wait_for_completion(timeout_seconds=90)`
✅ 短い方(B)が先に完了し、結果が返る

🤖 `wait_for_completion(timeout_seconds=90)`
✅ 残り(A)の結果が返る


## 4. コンソール切り替え

### 4.1 ユーザー起動コンソールへの切り替え
👤 別の pwsh ウィンドウで `Start-McpServer` を実行

🤖 `invoke_expression('Write-Output "Test"')`
✅ "Console switched. Pipeline NOT executed" メッセージ

🤖 `invoke_expression('Write-Output "Test"')`
✅ 正常に実行される

### 4.2 busy コンソールからの切り替え
🤖 `invoke_expression('Start-Sleep -Seconds 120; Write-Output "Slow"')`
👤 約10秒後に停止ボタンを押す

👤 別の pwsh ウィンドウで `Start-McpServer` を実行

🤖 `invoke_expression('Write-Output "Fast"')`
✅ ユーザー起動コンソールに切り替わり実行

🤖 `wait_for_completion(timeout_seconds=150)`
✅ 元のコンソールの結果 "Slow" が返る

---

## 5. エッジケース

### 5.1 コンソールの強制終了
🤖 `invoke_expression('Start-Sleep -Seconds 60; Write-Output "Test"')`
👤 停止ボタンを押す
👤 PowerShell コンソールウィンドウを閉じる

🤖 `wait_for_completion(timeout_seconds=30)`
✅ エラーにならない（dead pipe として処理される）

### 5.2 二重キャッシュ消費の防止
🤖 `invoke_expression('Start-Sleep -Seconds 30; Write-Output "Once"')`
👤 停止ボタンを押す

🤖 `wait_for_completion(timeout_seconds=60)`
✅ "Once" が返る

🤖 `wait_for_completion(timeout_seconds=10)`
✅ "No busy consoles to wait for." が返る（二重取得されない）

### 5.3 [CACHED] プレフィックスの動作
🤖 `invoke_expression('Start-Sleep -Seconds 200; Write-Output "Long"')`
（約3分待ってタイムアウトさせる）
✅ "[CACHED]" がレスポンスに含まれない（Proxy が除去）

---

## 6. get_current_location との統合

### 6.1 get_current_location でのキャッシュ回収
🤖 `invoke_expression('Start-Sleep -Seconds 20; Write-Output "BG"')`
👤 停止ボタンを押す

🤖 約25秒後に `get_current_location()`
✅ "BG" の結果と現在のロケーションが返る

---

## 実行順序の推奨

1. 2.4 → 1.1 → 1.2 （基本動作）
2. 2.2 → 2.3 （wait_for_completion の基本）
3. 3.1 （複数コンソール）
4. 5.2 （二重消費防止）
5. 必要に応じて他のケース
