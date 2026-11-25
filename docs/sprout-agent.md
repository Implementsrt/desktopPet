# 小芽的本地 Agent 接口

启动小芽后，右键选择「启用本地 Agent 接口」。每次启动时默认关闭。开启后，同一个 Windows 用户的本地程序可以连接命名管道 `\\.\pipe\DesktopPet.Sprout.v1`。

每次连接发送一行 ASCII 命令，以 `\n` 或 `\r\n` 结尾。读到一行 UTF-8 JSON 回应后关闭连接。管道串行处理请求。

| 命令 | 返回 |
| --- | --- |
| `status` | `ok`、协议 `version`、当前 `personality`、`state` 和 `onWindow` |
| `capabilities` | `ok`、协议 `version` 和当前版本支持的 `actions`。连接程序可用它判断哪些动作能调用 |
| `sit` | 有可坐的窗沿时坐下。否则返回 `needs_window` |
| `fireworks` | 放一次烟花。等待中的技能返回 `cooldown` |

成功的动作返回 `{"ok":true}`。正在拖动、攀爬、休息或运行动作时，新的动作请求可能收到 `busy`。未知命令返回 `unknown_command`。`state` 的取值为 `awake`、`sleeping`、`climbing`、`dragging`、`airborne` 或 `playing`。

PowerShell 查询示例：

```powershell
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'DesktopPet.Sprout.v1', 'InOut')
try {
    $pipe.Connect(2000)
    $writer = [IO.StreamWriter]::new($pipe)
    $writer.AutoFlush = $true
    $reader = [IO.StreamReader]::new($pipe)
    $writer.WriteLine('status')
    $reader.ReadLine() | ConvertFrom-Json
} finally {
    $pipe.Dispose()
}
```

后续接 Gemini 时，由独立的本地程序调用模型，再把模型选中的动作发送给小芽。模型认证放在接入程序里。小芽本身通过这个管道提供状态和动作。

## 后续可以接什么

- 对话和个性由 Agent 处理。可以先用输入框聊，回复显示成桌宠气泡，回复时搭配招手、惊讶或庆祝等本地表情。
- Agent 可以按自然语言请求一组本地动作，例如「坐在窗沿等我」或「我写完了，放个烟花」。本地检查当前状态、可达的窗沿和技能冷却，返回动作是否执行。
- 桌宠可把本地事件描述交给 Agent，例如「刚掉下来」或「醒了」，让回复符合刚发生的事。基础走路、看窗沿、攀爬、碰撞、休眠和自动玩耍由本地运行，不依赖模型请求。
- 动作名称沿用稳定的英文标识。模型适配程序可以换成别的模型或关闭。鼠标拉拽等已有桌宠玩法由本地行为处理。
