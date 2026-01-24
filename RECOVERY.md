# 復旧手順: メインブランチに戻る方法

もし `feature/json-rpc-invoke-expression` ブランチの変更で PowerShell.MCP が動かなくなった場合：

```powershell
cd C:\MyProj\PowerShell.MCP
git checkout main
.\Build-AllPlatforms.ps1 -Target Dll,WinX64
```

これで元の動作する状態に戻ります。