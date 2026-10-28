# 场景道路碰撞

场景：`Assets/Scenes/SampleScene.scene`。

导入模型含有木板断面、接缝和不连续坡道，直接把每块模型当成行驶碰撞面，会出现掉穿、卡在木板竖直端面和碰撞偏航。场景使用独立的连续游戏碰撞面，可见模型保留。

- 中央道路采用平滑高度网格，下方另有连续厚底支撑。
- 两条坡道使用闭合、有厚度的连续纵向剖面，覆盖平台出口、木板接缝及竞技场接入口。
- 出生平台使用平整支撑；平台、坡道和场地边缘有阻挡碰撞。
- 旧的高位代理方盒、细碎的行驶面碰撞停用，避免重复接触和卡边。
- 道路使用低摩擦物理材质，由现有控制器管理滚动阻力和侧向抓地。
- 常规驾驶通过显式旋转控制转向及坡度；真实车辆撞击期间仍允许原生偏航，恢复后重新锁定。

生成的碰撞网格和物理材质位于 `Assets/Physics/RoadSupport/`。编辑菜单 `Tools > Bumper Cars > Fix Road Collisions` 会重复生成、检查并保存；车辆重新绑定时也会调用同一配置。

`Tools > Bumper Cars > Validate Road Grounding` 和 `Validate Driving Routes` 均需在暂停的 Play 模式运行。测试让真实车辆副本自由行驶，覆盖正常加速、最高速度、场地四边阻挡、显式转向及撞击旋转恢复；不冻结车辆水平位置。报告为 `Logs/RoadDrivingValidation.txt`，采样轨迹为 `Logs/RoadDrivingTrajectory.csv`。

当前修复前的场景与失败轨迹保存在 `Logs/RoadContinuityBackup-20261027/`。运行测试不保存车辆临时位置，结束后删除副本并恢复模拟状态。
