# 小芽的本地 Agent 接口

## Gemini 聊天

在小芽身上点右键，打开「Gemini 密钥」，将 API 密钥粘贴到密码输入框并保存。Windows 会用当前用户的加密凭据保护密钥，保存到 `%LOCALAPPDATA%\DesktopPet\Sprout\gemini.key`。在同一菜单中可清除密钥。

右键选「和小芽聊天」，输入文字后按回车。回复会显示在聊天窗口和小芽头顶。聊天和小芽自己的事件保存在 `%LOCALAPPDATA%\DesktopPet\Sprout\chat-memory.dat`，由 Windows 当前用户加密。最多保留 160 条聊天和 30 条事件，关闭窗口或退出程序后仍可恢复。在聊天里的「设置」可清除。旧版已经丢掉的聊天无法恢复。

聊天通过 `127.0.0.1:7890` 访问 Gemini 3.5 Flash-Lite，使用当前文字、性格、最近 12 条聊天、少量匹配的旧消息和最近 6 条桌宠事件。模型优先直接回答问题，明确区分已验证的信息和建议。原始提示词可在 `FormPet.SproutChat.cs` 的 `ChatInstructions` 和 `AutoInstructions` 查看。

明确让小芽找桌宠素材时，程序会使用固定关键词通过同一代理查公开 GitHub 仓库，返回链接和仓库许可证。请在引用角色图前检查素材有没有单独的许可证。GitHub 不接收聊天记录、桌面状态或 Gemini 密钥。

保存密钥后，小芽也可以主动发言。启动至少 25 秒后，桌面或小芽的事件有变化时可以询问 Gemini。主动请求至少间隔 45 秒，状态没变至少等 3 分钟。休眠、全屏和聊天时暂缓。配额按 Google AI Studio 中项目的 [有效限制](https://aistudio.google.com/rate-limit) 执行。收到 429 或 503 时使用 Google 返回的等待时间，未给出时逐次延长等待。右键取消「允许小芽主动发言」可暂停。

主动请求只包含简化桌面状态、小芽的性格和它自己的少量近期事件，例如爬上窗顶或睡醒。前台只区分桌面、小芽、同屏窗口、其他屏窗口或全屏。窗口数量只分无、一个、少量和多个。光标只分靠近小芽、同屏和其他屏。这个接口不提供窗口标题、截图、文件、剪贴板内容或其他进程的内存。主动请求不携带聊天记录。

## 外部程序调用

启动小芽后，右键选择「启用本地 Agent 接口」。每次启动时默认关闭。开启后，同一个 Windows 用户的本地程序可以连接命名管道 `\\.\pipe\DesktopPet.Sprout.v1`。

每次连接发送一行 UTF-8 命令，以 `\n` 或 `\r\n` 结尾，整行不超过 1024 字节（含换行）。读到一行 UTF-8 JSON 回应后关闭连接。管道串行处理请求。

| 命令 | 返回 |
| --- | --- |
| `status` | `ok`、协议 `version`、当前 `personality`、`state` 和 `onWindow` |
| `capabilities` | `ok`、协议 `version` 和当前版本支持的 `actions`。连接程序可用它判断哪些动作能调用 |
| `context` | 读取主动发言所用的简化桌面状态。 |
| `say 一句话` | 直接显示本地气泡，最多 130 字，无需调用 Gemini。两次发言至少间隔 5 秒。 |
| `sit` | 有可坐的窗沿时坐下。否则返回 `needs_window` |
| `fireworks` | 放一次烟花。等待中的技能返回 `cooldown` |

成功的动作返回 `{"ok":true}`。正在拖动、攀爬、休息或运行动作时，新的动作请求可能收到 `busy`。未知命令返回 `unknown_command`。`state` 的取值为 `awake`、`sleeping`、`climbing`、`dragging`、`airborne` 或 `playing`。

PowerShell 查询示例：

```powershell
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'DesktopPet.Sprout.v1', 'InOut')
try {
    $pipe.Connect(2000)
    $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
    $reader = [IO.StreamReader]::new($pipe)
    $writer.WriteLine('say 构建完成啦！') # 也可以发送 status、context 或 capabilities
    $reader.ReadLine() | ConvertFrom-Json
} finally {
    $pipe.Dispose()
}
```

如果要接入其他 Agent，独立的本地程序可以通过这个管道读取小芽状态并调用动作。内置 Gemini 聊天可以直接使用右键菜单。

## 后续可以接什么

- 对话已经可以用输入框，回复显示成桌宠气泡。后续可以搭配招手、惊讶或庆祝等本地表情。
- Agent 可以按自然语言请求一组本地动作，例如「坐在窗沿等我」或「我写完了，放个烟花」。本地检查当前状态、可达的窗沿和技能冷却，返回动作是否执行。
- 桌宠可把本地事件描述交给 Agent，例如「刚掉下来」或「醒了」，让回复符合刚发生的事。基础走路、看窗沿、攀爬、碰撞、休眠和自动玩耍由本地运行，不依赖模型请求。
- 动作名称沿用稳定的英文标识。模型适配程序可以换成别的模型或关闭。鼠标拉拽等已有桌宠玩法由本地行为处理。
