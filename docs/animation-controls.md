# 界面动画控制

“设置 → 外观 → 启用界面动画”默认关闭。窗口与控件过渡子开关默认开启，
平滑滚动、动态主题背景、长文本自动滚动默认关闭。总开关关闭时保留子开关选择。
Windows 关闭客户端动画时优先遵循系统设置；所有选项立即生效并保存至现有用户设置。

## 实现约定

- `AnimationSettings` 是主程序与插件共享的有效策略；由主程序在 UI 线程初始化和更新。
- XAML 状态透明度使用 `Motion.Opacity`：先设置静态目标值，仅在允许时创建短暂时钟。
- XAML 循环使用 `Motion.Loop`，主题装饰同时设置 `Motion.IsBackground`。
  普通控件循环只在已加载且实际可见时运行；停用、隐藏、卸载时移除时钟。
  VisualBrush 脱离普通视觉树：仅当前主题且至少一个应用窗口可见时运行，切换主题停掉旧背景。
- 代码过渡使用 `MotionTransition`：设置最终基值，正常结束或禁用时移除时钟并完成一次；
  新操作取代旧操作时取消旧完成回调。禁止仅修改 Duration 为零。
- 订阅跟随控件或动画生命周期，不扫描全局视觉树、不轮询动画状态。
- 媒体播放、实际进度更新、搜索防抖、业务计时器、静态阴影和透明度不属于动画开关。

## 覆盖清单

| 入口 | 策略与静态路径 |
|---|---|
| `QuickSearchWindowShowSupport` / `QuickSearchWindowController` | 过渡；直接显示/隐藏并执行焦点与清理逻辑，无淡出等待 |
| `ThemeManager` | 过渡；立即换资源，停止旧主题计时器，恢复最终透明度 |
| `QuickLookManagerPositioning` / `QuickLookWindow` | 过渡；直接定位，拖动取消旧滑入 |
| `QuickSearchLaunchPanel` | 过渡；宽度和文字直接到位；400 ms 名称阅读时间保留 |
| `Button.xaml` / `ListBox.xaml` / `Menu.xaml` | 过渡；悬停、按下、选中即时反馈 |
| `ScrollBar.xaml` / `SearchWindow.xaml` | 过渡；滚动条、拖动提示即时显隐 |
| `RippleAdorner` | 过渡；停用不创建波纹，已有波纹清理 |
| `SettingsSearchHighlight` | 总开关；静态定位边框保留 1.6 秒再移除 |
| `SearchBoxControl` 左右标志 | 总开关；静态忙碌标志，无呼吸和恢复时钟 |
| `Controls.xaml` Spinner / `ServiceSettingsPage` | 总开关；静态状态指示，无旋转 |
| `SmoothWheelScrollBehavior` | 滚动；立即退订 Rendering、清零惯性，恢复原生滚轮 |
| `MarqueeBehavior` / `FileOccupationView` | 跑马灯；文本复位，完整文本工具提示 |
| `WeatheringBlue` / `SakuraBloom` / `NeonGenesis` | 背景；移除循环，保留静态装饰 |
| `SettingsWindow.xaml` / `SettingsComboBox.xaml` / `ActionMenuItem.xaml` / `Menu.xaml` | 过渡；PopupAnimation 动态绑定为 None |
| `ActionFlyout` / `PluginContextMenuHelper` | 过渡；代码创建的 Popup 使用相同动态绑定 |
| WPF 系统菜单、下拉框、工具提示资源 | 过渡；覆盖 PopupAnimation 系统资源键，恢复时重新读取系统值 |
| 自有顶层窗口 | 过渡；HWND 级 DWM 过渡禁用，不修改操作系统全局设置 |

独立官网、外部预览程序以及未接入 SDK 的第三方插件不受强制控制。

## 验证

`AnimationControlTests` 覆盖旧配置默认值、JSON 往返、全部策略组合、
即时完成、过期回调取消、运行中禁用、循环隐藏/恢复，以及真实列表模板的静态选中反馈。
Portable x64 工作流单独执行动画与相关滚动、跑马灯、设置索引测试，随后发布主程序和插件。

发布前逐项操作上述入口，检查关闭、开启、运行中切换、隐藏后再显示、主题连续切换和重启持久化。
复扫 `BeginAnimation`、`BeginStoryboard`、`Storyboard.Begin`、`PopupAnimation`、
`CompositionTarget.Rendering` 和动画专用计时器，新增入口也须说明其关闭路径。
记录相同数据和主题下的窗口显隐、主题切换、滚动和动态背景 CPU/GPU 对比；
编译和自动化测试通过不代表实机性能已经测量。
