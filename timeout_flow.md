# タイムアウト時のフロー

```mermaid
sequenceDiagram
    participant MCP as MCP Client
    participant Server as NamedPipeServer<br>(HandleClientAsync)
    participant Wait as WaitForResult()
    participant PS as PowerShell<br>実行スレッド
    participant State as ExecutionState

    MCP->>Server: invoke_expression
    Server->>State: SetBusy(pipeline)
    Server->>Wait: WaitForResult()
    Wait->>Wait: WaitOne(170s)
    
    activate PS
    Note over PS: コマンド実行中...
    
    Note over Wait: 170秒経過<br>タイムアウト
    Wait->>State: MarkForCaching()
    Note over State: ShouldCacheOutput = true
    Wait-->>Server: "Command is still running..."
    
    Note over Server: isCommandStillRunning = true
    Server-->>MCP: "Command is still running..."
    Note over Server: CompleteExecution()<br>呼ばない
    
    Note over PS: コマンド完了
    PS->>State: NotifyResultReady(result)
    deactivate PS
    
    Note over State: ShouldCacheOutput == true
    State->>State: AddToCache(result)
    State->>State: CompleteExecution()
    Note over State: Status: completed
    
    MCP->>Server: wait_for_completion
    Server->>State: ConsumeOutput()
    State-->>Server: cached result
    Server-->>MCP: result
    Note over State: Status: standby
```

## 状態遷移

| タイミング | _isBusy | ShouldCacheOutput | Status |
|-----------|---------|-------------------|--------|
| コマンド開始 | true | false | busy |
| 170秒タイムアウト | true | true | busy |
| コマンド完了 (NotifyResultReady) | false | false | completed |
| キャッシュ消費後 | false | false | standby |
