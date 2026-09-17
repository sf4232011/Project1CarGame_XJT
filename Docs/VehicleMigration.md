# Octopus / Toad 车辆接入记录

场景：`Assets/Scenes/SampleScene.scene`。已在团结编辑器 1.8.4（2022.3.62t6）内完成组件绑定并保存。

| 车辆 | 玩家与驾驶按键 | 技能 | 相机 |
| --- | --- | --- | --- |
| Toad | Player1，W/S 前进倒车，A/D 转向 | 按住 F 生成减速轨迹 | BlueCamera，左半屏 |
| Octopus | Player2，上/下方向键前进倒车，左/右方向键转向 | 句号键 `.` 喷墨并后退，8 秒冷却 | RedCamera，右半屏 |

沿用原项目双人分屏分配；原项目没有车辆选择/切换系统。旧 `Toad(blue)` 保留在场景中并停用。两辆模型根节点和原有 78 个 Transform 的位置、旋转、缩放均保留，未替换网格、材质或骨骼。

## 已绑定的组件

两车根节点均绑定 `Rigidbody`（沿用质量 500）、适配尺寸的 `BoxCollider`、`BumperCarController`、`BumperCarHealth`、`BumperCarCollisionDamage`、`BumperCarImpactFeedback`、`AudioSource` 和 `BumperCarWheelVisuals`。

- 新增 `GroundCheck` 和前/中/后三段伤害触发器，显式绑定各自的 owner。
- Toad 前轮：`FrontTire_L`、`FrontTire_R`；后轮：`BackTire_Left`、`BackTire_Right`。
- Octopus 前轮：`tire46`、`tire48`；后轮：`tire45`、`tire47`。
- 原驾驶使用刚体受力，并不依赖 WheelCollider。车轮组件负责滚动和前轮转向显示，兼容导入模型的偏移枢轴及镜像缩放。
- Toad 绑定 `ToadSlowTrailSkill`、`SlowZone` 预制体、`SlowTrailSpawn` 和已有能量条。
- Octopus 绑定 `RedCarInkRetreatSkill`、`InkSprayHitbox` 预制体、`InkSpawnPoint` 和已有冷却 UI。
- 重新绑定 GameManager 两位玩家、血量、HUD、分屏相机；保留比赛时长 200 秒、墨水遮罩和胜负界面。
- 启用两台跟随相机和一个 AudioListener；停用额外 `Camera` 与 Toad 下的 `back` 相机，避免覆盖分屏。Toad 后续已改为匹配 Octopus 的后上方跟随视角，详见 `ToadCameraMatching.md`。
- 原平台只有可见网格，没有碰撞。为 `487`、`1485` 下的 48 个平台、坡道网格补充静态 MeshCollider，避免两辆车穿过出生平台。
- 碰撞音效沿用现有运行时生成逻辑。原车没有绑定碰撞/喷墨粒子，本次保持这两项为可选引用。

## 修改文件

| 文件 | 内容 |
| --- | --- |
| `Assets/Scenes/SampleScene.scene` | 车辆组件、轮胎、技能、物理、相机、HUD 与平台碰撞绑定 |
| `Assets/Scripts/BumperCars/BumperCarController.cs` | 支持独立接地点，接地时忽略自身刚体；公开控制状态和转向显示值；转向平滑使用固定物理步长 |
| `Assets/Scripts/BumperCars/BumperCarCameraFollow.cs` | 跟随距离使用世界单位，避免模型 160/220 倍缩放放大相机距离 |
| `Assets/Scripts/BumperCars/BumperCarImpactFeedback.cs` | 尊重 AudioSource 的空间音效配置，释放生成的 AudioClip |
| `Assets/Scripts/BumperCars/RedCarInkRetreatSkill.cs` | 适配喷墨高度；禁用控制后禁止技能及继续施加后退力 |
| `Assets/Scripts/BumperCars/ToadSlowTrailSkill.cs` | 适配轨迹尺寸；禁用控制后停止技能输入 |
| `Assets/Scripts/BumperCars/BumperCarWheelVisuals.cs` | 新增四轮显示组件，保持轮胎中心固定 |
| `Assets/Editor/BumperCarVehicleSetup.cs` | 新增可重复执行的绑定及引用检查菜单 |

新增脚本的 `.meta` 文件由编辑器生成。

## 验证结果

- 编辑器编译：0 错误、0 警告。
- 最后一轮运行检查后 Console：0 错误、0 警告，未发现空引用异常。
- 78 个原模型 Transform 与迁移前比较：0 处变化。
- 27 项脚本驱动的 Play Mode 检查全部通过：平台支撑与驶离出生点、独立前进/倒车/转向、车轮滚动、碰撞伤害/音效/震动、减速区域、喷墨/后退/冷却、技能排除自身、胜负 UI 与控制锁定。
- 独立驾驶测试在物理模拟中注入车辆控制输入；并非真实键盘端到端测试，也未进行长时间人工试玩。按键映射沿用现有代码并核对了各自玩家编号。
- 检查过两车分屏的运行截图。测试结束已退出 Play Mode，保存的是编辑状态的原始模型摆放。

无需手动拖拽组件。可用 `Tools > Bumper Cars > Validate Octopus and Toad` 重新检查必要引用；`Bind Octopus and Toad` 可重新执行本场景的绑定。
