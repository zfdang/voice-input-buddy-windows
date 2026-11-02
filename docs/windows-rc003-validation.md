# RC003 Windows 实机排查记录

测试环境：Windows 11，蓝牙直连小米语音遥控器，设备标识 VID 2717 / PID 32B8，VB-CABLE。

## 已复现并修正的问题

- 蓝牙接口使用 `Dev_VID&012717` 路径，旧白名单未包含该格式。
- BLE 设备是 AssociationEndpoint，须用对应种类及 `System.Devices.Aep.ContainerId` 查询；HID 接口则经设备实例查询设备节点的 Container ID。两个通道现在能匹配同一实体设备，并排除另一只同厂商键盘。
- 向导将第四步之前的按键当成测试通过。现在只接受第四步内的新按键，重新进入会重置结果。
- 遥控器 OK 同时是 Enter，会触发焦点所在的“下一步”。第四步现在处理窗口内的导航键，Raw Input 仍单独接收按键事件。
- 本机遥控器直接发送 ATVV `AudioStart`，没有先发 `StartSearch`。旧实现会在 Idle 状态立即关麦。现在接受就绪连接的新会话，同时保留取消、结束会话及重复旧会话保护。
- `hDevice=0` 的注入事件被测试环境特例放行，形成 SendInput 反馈循环。现已拒绝没有实体来源的输入。

## 按键覆盖

| 按键 | 本机事件与状态 |
| --- | --- |
| 上、下、左、右、OK | Raw Keyboard 按下、松开均收到；方向键及 OK 已实机验证，OK 不再跳页 |
| Home、Menu | Raw Keyboard 事件已收到 |
| TV | 收到 VKey C0 / MakeCode 29；LiveTv 映射已实机验证 |
| 电源 | 收到 VKey FF / MakeCode 5E；Power 映射已实机验证，其他 FF 不应判为 Power |
| 语音 | ATVV AudioStart/AudioStop；已实机验证第五步电平响应及松开结束 |
| Back、音量加、音量减 | 本机逐键重复操作时未收到 Raw Input；当前尚未支持 |

Back/音量排查：HID GATT 服务 1812 的特征枚举返回 AccessDenied。另行订阅可访问厂商服务 8a7a0001 下三个 Notify 特征，持续监听期间逐键按压仍无通知；同轮“上”键能正常到达 Raw Input。该结果只描述本机与本次固件，不代表所有遥控器或 Windows 版本。

## 验证边界

自动测试覆盖连接就绪状态恢复、过期连接标志拒绝、第四步新按键要求、记录到的 Windows 键码及注入事件拒绝。输入法真实转写、主界面映射与所有恢复场景仍须单独实机验收，不能用音量条跳动代替转写成功。

诊断只保存在本地；不记录语音音频或转写文本。

## 微信输入法联动排查与优化

- 微信默认语音快捷键为左 Ctrl + 左 Win。预设现已支持，快捷键解析器支持仅含修饰键的组合。
- 在 `NativeInputSender` 中为 Win 键（`VK_LWIN`、`VK_RWIN`）及方向键等扩展键补齐了 `KEYEVENTF_EXTENDEDKEY` 标志，确保全局钩子与输入法热键层能够准确识别 Win 键组合。
- 优化了遥控器固定 `sessionId = 0` 导致二次按键被拒的缺陷：通过 500ms 窗口防抖并在 `AudioStop`/断连/新会话启动时清除 `_lastEndedSessionId`，避免了遥控器在首轮会话结束后永久无法开麦的问题。
- 优化了向导第四步按键过滤：保留方向键与回车（OK 键）防跳页拦截的同时，解除了对物理键盘 `Escape` 的屏蔽，恢复了常规窗口退出交互。
- 协同了 `RemoteVoiceKeyGuard` 与自定义快捷键：当用户将语音快捷键配置为包含 F5 的组合（如 `Ctrl + F5`）时，Guard 自动禁用拦截且不注入伪造的 `F5 KeyUp`，确保 `Ctrl + F5` 能够被输入法正常接收并持续触发；当使用 `Ctrl + Win` 等不含 F5 的快捷键时，Guard 继续拦截物理 F5 连发以防干扰。
- 第七步真实转写仍需在实机环境下结合所选快捷键（如 `Ctrl + Win` 或 `Ctrl + F5`）进行最终验收。
