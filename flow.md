# NotifyResultReady から Named Pipe 応答までのフロー

## 設計概要

- `NotifyResultReady()` は常にキャッシュに追加
- `WaitForResult()` は `(isTimeout, shouldCache)` を返す
- Status の種類:
  - **Busy**: コマンド実行中（タイムアウト）
  - **Completed**: コマンド完了、キャッシュに保存済み（MCP client 切断済み）
  - **Ready**: コマンド完了、結果を返却

## 通常完了ケース

```mermaid
sequenceDiagram
    participant Handler as HandleClientAsync
    participant Wait as WaitForResult
    participant State as ExecutionState
    participant Notify as NotifyResultReady

    Handler->>Wait: ExecuteInvokeExpression()
    Wait->>Wait: WaitOne(170s) ブロック

    Note right of Notify: コマンド完了
    Notify->>State: AddToCache(result)
    Notify->>State: CompleteExecution()
    Notify->>Wait: Set()

    Wait-->>Handler: (false, false)
    Handler->>State: ConsumeCachedOutputs()
    Handler->>Handler: SendMessage(result)
    Note right of Handler: Status: Ready
```

## タイムアウトケース

```mermaid
sequenceDiagram
    participant Handler as HandleClientAsync
    participant Wait as WaitForResult
    participant State as ExecutionState
    participant Notify as NotifyResultReady

    Handler->>Wait: ExecuteInvokeExpression()
    Wait->>Wait: WaitOne(170s) ブロック
    Note right of Wait: 170秒経過
    Wait->>State: MarkForCaching()
    Wait-->>Handler: (true, false)
    Handler->>Handler: SendMessage(timeout)
    Note right of Handler: Status: Busy

    Note right of Notify: 後でコマンド完了
    Notify->>State: AddToCache(result)
    Notify->>State: CompleteExecution()
```

## 停止ボタンケース

```mermaid
sequenceDiagram
    participant Proxy as exe proxy
    participant Handler as HandleClientAsync
    participant Wait as WaitForResult
    participant State as ExecutionState
    participant Notify as NotifyResultReady

    Note over Proxy: MCP client 停止ボタン
    Proxy->>Handler: get_status
    Handler->>State: MarkForCaching()
    Handler-->>Proxy: busy

    Note right of Notify: pause 完了
    Notify->>State: AddToCache(result)
    Notify->>State: CompleteExecution()
    Notify->>Wait: Set()

    Wait-->>Handler: (false, true)
    Handler->>Handler: SendMessage(Completed)
    Note right of Handler: Status: Completed
    Note right of Handler: キャッシュ消費しない
```

## 停止後にスイッチしたとき

```mermaid
sequenceDiagram
    participant Handler as HandleClientAsync
    participant Wait as WaitForResult
    participant State as ExecutionState
    participant Notify as NotifyResultReady

    Handler->>Wait: ExecuteInvokeExpression(Get-Date)

    Note right of Notify: Get-Date 完了
    Notify->>State: AddToCache(Get-Date結果)
    Notify->>State: CompleteExecution()
    Notify->>Wait: Set()

    Wait-->>Handler: (false, false)
    Handler->>State: ConsumeCachedOutputs()
    Note right of State: done#1 + Get-Date結果
    Handler->>Handler: SendMessage(combined)
    Note right of Handler: Status: Ready
```

## 状態遷移

| 状態 | Status | キャッシュ消費 | 説明 |
|------|--------|--------------|------|
| 実行中 | Busy | しない | 170秒タイムアウト |
| 完了 (要キャッシュ) | Completed | しない | MCP client 切断済み |
| 完了 (通常) | Ready | する | 結果を返却 |
