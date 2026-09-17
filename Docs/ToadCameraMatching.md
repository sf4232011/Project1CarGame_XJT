# Toad 相机与 Octopus 匹配

已直接保存至 `Assets/Scenes/SampleScene.scene`，不需要手动绑定。

| 参数 | Octopus / RedCamera（保持原值） | Toad / BlueCamera |
| --- | --- | --- |
| 相机相对位置，世界单位 | `(0, 9, -17.50)` | `(0.116, 15.993, -30.339)` |
| 观察点相对位置，世界单位 | `(0, 2.4, 0)` | `(0.116, 3.382, 3.098)` |
| 俯角 | 约 20.7° | 约 20.7° |
| FOV | 60° | 60° |
| 位置平滑时间 | 0.08 秒 | 0.08 秒 |
| 旋转跟随速度 | 14 | 14 |
| 震动衰减 | 4.5 | 4.5 |
| 最大震动位移 | 0.28 | 0.535，随观察距离缩放 |

以实际静态网格和蒙皮姿态计算画面范围。Toad 的相机到观察点距离约为参考相机的 1.911 倍，取景中心均约为各自视口的 `(0.49, 0.52)`。因蛤蟆车更宽、章鱼车更高，在相同透视下匹配画面范围的对角线尺寸；没有拉伸模型来强行匹配长宽比。

为保留这个角度并避免出生点树叶遮挡，BlueCamera 额外挂载 `BumperCarCameraFoliageVisibility`。它只在 Toad 相机渲染期间临时隐藏挡住车辆的植物，渲染完成或下一台相机开始渲染前恢复原显示状态；不会修改植物材质、图层、碰撞或启用状态。Octopus 没有此组件。

## 本次修改

- `Assets/Scenes/SampleScene.scene`：Toad 相机参数、观察点和专属遮挡组件绑定。
- `Assets/Scripts/BumperCars/BumperCarCameraFollow.cs`：新增默认零值的车身局部观察偏移；跟随计算提取为显式时间步长方法。Octopus 使用零偏移，原行为不变。
- `Assets/Scripts/BumperCars/BumperCarCameraFoliageVisibility.cs`：新增仅影响绑定相机的植物遮挡处理。
- `Assets/Editor/BumperCarCameraMatching.cs`：新增 `Tools > Bumper Cars > Match Toad Camera to Octopus` 菜单，可按当前参考相机和两车尺寸重新匹配。
- `Assets/Editor/BumperCarVehicleSetup.cs`：车辆重新绑定时也使用匹配后的相机规则，避免恢复成旧的俯视配置。

## 验证

- 16 项脚本驱动的 Play Mode 检查通过：FOV/平滑参数、跟随目标、分屏、静止取景中心/尺寸/俯角、静止收敛、行驶跟随、转向跟随、车辆完整入镜、独立输入，以及植物显示恢复。
- 行驶与转向使用原车辆控制器和物理模拟驱动，不是长时间真实键盘试玩。
- 核对 Octopus 的 Camera、跟随组件及 Transform 序列化快照：修改前后完全一致。
- 编译及运行 Console 检查：0 错误、0 警告。
- 车辆控制、玩家分配、左右分屏、现有相机启用关系均保留。
