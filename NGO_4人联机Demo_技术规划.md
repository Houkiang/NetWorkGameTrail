# Unity NGO 4人联机Demo 技术规划

## 1. 项目目标

- 使用 Unity NGO（Netcode for GameObjects）实现 `Dedicated Server + Client` 架构。
- 允许最多 4 名玩家加入同一场景。
- 玩家可在场景中移动，并能看到其他玩家的实时位置变化。
- 仅实现最小可运行闭环，不包含战斗、匹配大厅、持久化等扩展功能。

## 2. 当前环境与约束

- Unity 版本：`2022.3.62f3c1`。
- 当前工程尚未安装 NGO/Transport 相关包。
- Demo 目标优先级：`先跑通，再优化`。

## 3. 技术选型

- 网络框架：`com.unity.netcode.gameobjects`（选择与 2022 LTS 兼容的稳定版本）。
- 传输层：`com.unity.transport`（Unity Transport）。
- 架构模型：服务器权威（Server Authoritative）。
- 连接方式：直连 IP + Port（本地/局域网优先）。

## 4. 网络架构设计

- 进程角色：
- `Server`：负责连接管理、玩家生成、状态同步权威判定。
- `Client`：采集本地输入并提交到服务器，接收同步结果并渲染。
- 场景模型：
- 所有玩家进入同一个 Gameplay 场景。
- 服务器在玩家连接时生成玩家对象（NetworkObject）。
- 同步策略：
- 移动输入通过 `ServerRpc` 上送。
- 位置/旋转由服务器更新并通过 `NetworkTransform` 同步。

## 5. 功能拆分与里程碑

## M0：基础准备

- 安装并锁定 NGO 与 Unity Transport。
- 创建 `NetworkManager` 预制体并配置传输端口（如 `7777`）。
- 创建 `PlayerPrefab`，挂载 `NetworkObject` 与移动脚本。

## M1：连接与玩家生成

- 实现启动入口 `NetworkBootstrap`：
- 可启动 `Host` / `Server` / `Client`。
- 支持通过参数或 Inspector 填写目标 IP 与端口。
- 配置 `ConnectionApproval`：
- 限制最大连接人数为 4。
- 超员时拒绝连接并返回提示原因。
- 在 `OnClientConnectedCallback` 中生成玩家对象。

## M2：移动与可见性同步

- 客户端读取输入（WASD/方向键）。
- 使用 `ServerRpc` 上传输入向量。
- 服务器执行移动逻辑（`CharacterController` 或 `Rigidbody` 二选一）。
- 通过 `NetworkTransform` 同步玩家位姿，使所有客户端彼此可见。

## M3：稳定性与可用性

- 处理断开重连与对象销毁：
- `OnClientDisconnectCallback` 时清理对应玩家对象。
- 增加基础日志：
- 连接成功、拒绝、断开、玩家生成、玩家销毁。
- 加入基础防护：
- 对输入向量做归一化与速度上限限制。

## M4：打包与联调

- 产物 A：`Server`（建议 Linux Server Build）。
- 产物 B：`Client`（Windows/Mac 任一）。
- 联调方案：
- 1 个服务器进程 + 2~4 个客户端实例。
- 验证 4 人上限、移动同步、断线回收是否正常。

## 6. 建议脚本结构

- `Assets/Scripts/Network/NetworkBootstrap.cs`
- 负责启动网络角色、读取 IP/Port、统一日志输出。
- `Assets/Scripts/Network/ConnectionApprovalHandler.cs`
- 负责连接审批与 4 人限制。
- `Assets/Scripts/Player/NetworkPlayerController.cs`
- 仅处理网络玩家移动逻辑（输入上行 + 服务器权威位移）。
- `Assets/Scripts/Player/PlayerSpawnService.cs`
- 管理客户端ID与玩家实体映射、生成与销毁。

## 7. 场景与预制体规范

- 场景：`Assets/Scenes/Gameplay.unity`。
- 预制体：
- `NetworkManager`：全局唯一，`DontDestroyOnLoad`。
- `PlayerPrefab`：包含 Mesh/材质、`NetworkObject`、`NetworkTransform`、`NetworkPlayerController`。
- 地图中加入简单平面与障碍，方便观察同步效果。

## 8. 验收标准（Definition of Done）

- 服务器可独立启动并监听端口。
- 客户端可连接到服务器并进入同一场景。
- 1~4 名玩家均可移动，互相可见实时位置变化。
- 第 5 名玩家连接被拒绝，并有可读提示。
- 任意客户端断开后，其他客户端中对应角色及时消失。

## 9. 测试清单

- 单机本地多开测试：Host + 2 Client。
- 专用服务器测试：Server + 4 Client。
- 压力边界测试：第 5 个客户端连接请求。
- 异常测试：运行中强制关闭某客户端，观察对象回收。
- 网络波动测试：人为增加延迟后观察移动抖动与可接受性。

## 10. 风险与后续扩展

- 潜在风险：
- 仅用 `NetworkTransform` 在高延迟下可能出现抖动。
- 未做客户端预测与回滚时，手感可能偏“钝”。
- 后续扩展方向：
- 加入 Relay/Lobby（跨公网更稳定）。
- 加入客户端预测与插值平滑。
- 加入基础房间UI、准备状态与重连流程。

## 11. 建议开发顺序（建议 2~3 天完成）

- Day 1：完成 M0 + M1（能连、能生人、限制 4 人）。
- Day 2：完成 M2（可移动、可见同步）+ M3（断线清理）。
- Day 3：完成 M4 联调与问题修复，输出可演示版本。

