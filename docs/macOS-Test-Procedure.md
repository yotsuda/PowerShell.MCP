# PowerShell.MCP macOS テスト手順書

## 概要

Scaleway M1 Mac mini を使用して PowerShell.MCP の macOS 動作確認を行う。

## 1. Scaleway Mac mini M1 のセットアップ

### 1.1 アカウント作成・インスタンス起動

1. https://console.scaleway.com にアクセス
2. アカウント作成（クレジットカード登録必要）
3. **Bare Metal** > **Apple Silicon** > **Mac mini M1** を選択
4. リージョン: `Paris 3` (PAR3)
5. OS: `macOS Sequoia` または最新版を選択
6. SSH キーを登録
7. インスタンス作成（**最低24時間課金: 約€2.64**）

### 1.2 接続情報の確認

インスタンス作成後、以下を確認：
- **IP アドレス**: コンソールに表示
- **VNC パスワード**: コンソールで生成・確認

### 1.3 SSH 接続

```bash
ssh m1@<IP_ADDRESS>
```

### 1.4 VNC 接続（GUI テスト用）

macOS / Windows / Linux から VNC クライアントで接続：
- **アドレス**: `<IP_ADDRESS>:5900`
- **パスワード**: コンソールで確認したもの

---

## 2. PowerShell 7 のインストール

### 2.1 Homebrew インストール（未インストールの場合）

```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
```

インストール後、PATH に追加：
```bash
echo 'eval "$(/opt/homebrew/bin/brew shellenv)"' >> ~/.zprofile
eval "$(/opt/homebrew/bin/brew shellenv)"
```

### 2.2 PowerShell 7 インストール

```bash
brew install powershell/tap/powershell
```

### 2.3 インストール確認

```bash
pwsh --version
# 出力例: PowerShell 7.5.x
```

### 2.4 pwsh のパス確認

```bash
which pwsh
# 出力例: /opt/homebrew/bin/pwsh
```

> ⚠️ **重要**: Homebrew は `/opt/homebrew/bin` にインストールするが、
> 現在の `PwshLauncherMacOS` は `/usr/local/bin:/usr/bin:/bin` のみを PATH に設定している。
> これは修正が必要な可能性がある。

---

## 3. PowerShell.MCP のインストール

### 3.1 Option A: PowerShell Gallery からインストール（推奨）

```powershell
pwsh -Command "Install-Module PowerShell.MCP -Scope CurrentUser"
```

### 3.2 Option B: ソースからビルド

```bash
# .NET 9 SDK インストール
brew install dotnet@9

# リポジトリクローン
git clone https://github.com/yosbits/PowerShell.MCP.git
cd PowerShell.MCP

# ビルド（macOS 向け）
dotnet publish PowerShell.MCP.Proxy -c Release -r osx-arm64 -o ./out/osx-arm64
dotnet build PowerShell.MCP -c Release
```

---

## 4. テスト項目

### 4.1 モジュールインポートテスト

```powershell
pwsh
Import-Module PowerShell.MCP
```

**確認ポイント:**
- [ ] エラーなくインポートできる
- [ ] `[PowerShell.MCP] MCP server started` の Information メッセージ
- [ ] Named Pipe サーバーが起動している

### 4.2 Named Pipe 存在確認

```bash
ls -la /tmp/CoreFxPipe_*
# 期待: /tmp/CoreFxPipe_PowerShell.MCP.Communication が存在
```

### 4.3 Get-MCPProxyPath テスト

```powershell
Get-MCPProxyPath
# 期待: /path/to/PowerShell.MCP/bin/osx-arm64/PowerShell.MCP.Proxy
```

### 4.4 Proxy 単体起動テスト

別ターミナルで：
```bash
/path/to/PowerShell.MCP.Proxy
```

**確認ポイント:**
- [ ] Proxy が起動する
- [ ] stderr に `[INFO]` ログが出力される

### 4.5 start_powershell_console テスト（最重要）

**事前準備:**
1. pwsh を一度終了する（Named Pipe を閉じる）
2. Terminal.app を閉じる

**テスト手順:**

```bash
# Proxy を起動
/path/to/PowerShell.MCP.Proxy
```

MCP クライアント（または手動で JSON-RPC）から `start_powershell_console` を呼び出す。

**確認ポイント:**
- [ ] Terminal.app が新しいウィンドウで開く
- [ ] pwsh が起動している
- [ ] PowerShell.MCP モジュールがインポートされている
- [ ] Named Pipe 接続が確立される

### 4.6 invoke_expression テスト

```json
{
  "name": "invoke_expression",
  "pipeline": "Get-Process | Select-Object -First 5",
  "execute_immediately": true
}
```

**確認ポイント:**
- [ ] コマンドが実行される
- [ ] 結果が返却される
- [ ] Terminal.app にコマンドと結果が表示される

### 4.7 PSReadLine 無効化確認

```powershell
Get-Module PSReadLine
# 期待: 何も返らない（モジュールがロードされていない）
```

---

## 5. 既知の問題と確認事項

### 5.1 PATH 問題

**問題**: `PwshLauncherMacOS` が `/usr/local/bin:/usr/bin:/bin` のみを PATH に設定

**確認**: Homebrew の pwsh が見つからない可能性
```csharp
// PowerShellProcessManager.cs Line 196
var path = "/usr/local/bin:/usr/bin:/bin";
```

**修正候補**:
```csharp
var path = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";
```

### 5.2 AppleScript エスケープ問題

**問題**: HOME パスに `'` が含まれる場合

**確認**: ユーザー名に特殊文字がある場合の動作
```csharp
// Line 213
$"    do script \"env -i HOME='{home}' USER='{user}' ...
```

### 5.3 Terminal.app 以外のターミナル

**確認**: iTerm2 など他のターミナルがデフォルトの場合の動作

---

## 6. トラブルシューティング

### 6.1 pwsh が見つからない

```bash
# シンボリックリンク作成
sudo ln -s /opt/homebrew/bin/pwsh /usr/local/bin/pwsh
```

### 6.2 Named Pipe 接続タイムアウト

```bash
# Named Pipe の存在確認
ls -la /tmp/CoreFxPipe_*

# パーミッション確認
stat /tmp/CoreFxPipe_PowerShell.MCP.Communication
```

### 6.3 Terminal.app が開かない

```bash
# AppleScript を手動テスト
osascript -e 'tell application "Terminal" to activate'
osascript -e 'tell application "Terminal" to do script "echo test"'
```

### 6.4 モジュールインポートエラー

```powershell
# 詳細エラー確認
$ErrorActionPreference = 'Continue'
Import-Module PowerShell.MCP -Verbose
```

---

## 7. テスト完了後

### 7.1 結果記録

以下をメモ：
- macOS バージョン
- PowerShell バージョン
- 各テスト項目の Pass/Fail
- 発生したエラーメッセージ
- スクリーンショット（VNC経由で取得）

### 7.2 インスタンス削除

**重要**: 24時間経過後、不要であればインスタンスを削除して課金を止める

Scaleway コンソール > Apple Silicon > インスタンス選択 > Delete

---

## 8. 修正が必要と思われる箇所

優先度順：

1. **PATH に `/opt/homebrew/bin` を追加** - Homebrew インストールの pwsh が見つからない
2. **AppleScript のエスケープ処理** - 特殊文字対応
3. **Terminal.app 以外のターミナル対応** - iTerm2 等

---

## 参考リンク

- [Scaleway Apple Silicon](https://www.scaleway.com/en/apple-silicon/)
- [PowerShell on macOS](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-macos)
- [Homebrew](https://brew.sh/)
