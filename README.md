# SlapSurface

Vibe 出来的自用 Dalamud 插件界面样式库。

- 依赖 Dalamud ImGui 绑定和 FontAwesomeIcon，仅面向 Dalamud 插件
- 源码直接编进宿主插件（没有独立 csproj，类型 internal），复制或子模块引入都行
- 宿主接入前先注册：
  - `Slap.RegisterIconFont(...)` 图标字体
  - `Slap.RegisterGameIconLoader(...)` 游戏图标加载
  - `Slap.PushWindowFrameHostStyle(theme, metrics, typography)` 宿主样式