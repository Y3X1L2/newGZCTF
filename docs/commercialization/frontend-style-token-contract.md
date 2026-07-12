# 前端样式 Token 契约

## 1. 唯一来源

全局设计 Token 的唯一 CSS 来源为：

`src/GZCTF/ClientApp/src/styles/foundation/tokens.css`

`YinyuDesignLab.css`、`YinyuTheme.css` 和 `YinyuRefinement.css` 只能消费 Token 或提供迁移兼容映射，不得重新定义同名基础 Token。

## 2. Token 分类

### 颜色

- `--yy-color-bg-*`：页面背景层。
- `--yy-color-surface-*`：面板和浮层。
- `--yy-color-border-*`：普通、强调和交互边框。
- `--yy-color-text-*`：主文本、次文本和静默文本。
- `--yy-color-accent-*`：品牌、信息、警告、危险和成功语义。

颜色必须按语义使用。页面不得使用“某个绿色值”表达成功，也不得用品牌色表达所有状态。

### 空间与密度

- `--yy-space-1` 到 `--yy-space-8`。
- `--yy-density-compact`、`--yy-density-default`、`--yy-density-comfortable`。
- 页面外边距、区块间距和控件间距只能使用 Token 或 Mantine 对应 spacing。

### 字体

- `--yy-font-sans`、`--yy-font-display`、`--yy-font-mono`。
- `--yy-font-size-xs` 到 `--yy-font-size-2xl`。
- 视口宽度不能直接缩放字体；响应式只调整布局、留白和有限的离散字号。

### 形状与层级

- `--yy-radius-sm/md/lg`，卡片默认不超过 8px。
- `--yy-shadow-surface/overlay/focus`。
- `--yy-z-background/content/header/overlay/toast`。

### 动效

- `--yy-motion-fast`、`--yy-motion-normal`、`--yy-motion-slow`。
- `--yy-ease-standard`、`--yy-ease-emphasized`。
- 所有持续动画必须在 `prefers-reduced-motion: reduce` 下停止。

### 布局

- `--yy-page-max-width`：标准页面最大宽度。
- `--yy-page-gutter`：响应式页面边距。
- `--yy-sidebar-width`、`--yy-header-height`。
- 固定格式元素必须有稳定的 grid、min/max 或 aspect-ratio 约束。

## 3. 组件使用规则

允许：

```css
.panel {
  color: var(--yy-color-text-primary);
  border: 1px solid var(--yy-color-border-default);
  border-radius: var(--yy-radius-md);
  background: var(--yy-color-surface-1);
  transition: border-color var(--yy-motion-fast) var(--yy-ease-standard);
}
```

禁止：

```css
.panel {
  color: #f4f7f5;
  border: 1px solid rgba(221, 229, 223, 0.2);
  border-radius: 13px;
  transition: all 0.37s ease;
}
```

运行时几何值使用 CSS 自定义属性，不使用 inline 视觉常量：

```tsx
<div className={classes.progress} style={{ '--progress': `${value}%` } as CSSProperties} />
```

## 4. 响应式矩阵

| 视口 | 目标行为 |
| --- | --- |
| 360-479 | 单列；主操作不丢失；表格在自身容器滚动 |
| 480-767 | 单列或两列摘要；Drawer 占满可用宽度 |
| 768-1023 | 两列；工具栏可换行；侧栏可折叠为上方区块 |
| 1024-1439 | 标准桌面布局 |
| 1440-1919 | 扩展桌面，正文仍受最大宽度约束 |
| 1920+ | 不按视口放大字体；仅增加可用列数和面板宽度 |

## 5. 迁移策略

1. 先引入 Token 并将历史变量映射到新 Token。
2. 新公共组件只使用新 Token。
3. 每拆分一个高风险页面，就把该页面的新增样式迁入 CSS Module。
4. 历史全局选择器按页面回归结果删除，不能一次性全删。
5. 每次删除前使用浏览器检查桌面、笔记本和移动视口，并保留关键截图。

## 6. 退出标准

- 修改品牌色、页面背景、面板层级、圆角、字号或密度时，不需要编辑业务页面。
- 新页面无需复制 `yy-*` 全局选择器即可达到平台一致风格。
- CSS 扫描阻止新增裸十六进制颜色、页面级 `!important` 和无限持续动画。
- 全站在 reduced-motion 模式下保留信息但停止装饰动画。
