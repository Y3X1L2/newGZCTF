# vNext 样式 Token

正式前端的语义变量定义在 [vnext/design/tokens.css](../../../src/GZCTF/ClientApp/src/vnext/design/tokens.css)，前缀为 `--yn-`。变量名与可用值以该文件为准；本页只解释使用规则，避免复制一份完整清单。

## 常用分组

| 需要表达什么 | 当前变量示例 |
| --- | --- |
| 页面与面板 | `--yn-color-canvas`、`--yn-color-surface`、`--yn-color-surface-muted` |
| 文本与边界 | `--yn-color-text`、`--yn-color-muted`、`--yn-color-border` |
| 品牌与状态 | `--yn-color-brand`、`--yn-color-info`、`--yn-color-warning`、`--yn-color-danger` |
| 字体 | `--yn-font-sans`、`--yn-font-mono`、`--yn-font-sm`、`--yn-font-lg` |
| 间距与形状 | `--yn-space-1` 到 `--yn-space-8`、`--yn-radius-sm/md/lg` |
| 阴影与动效 | `--yn-shadow-surface/overlay/focus`、`--yn-motion-fast/normal`、`--yn-ease-standard` |
| 页面布局 | `--yn-rail-width`、`--yn-context-height`、`--yn-page-max-width`、`--yn-page-gutter` |

斜线是本表的分组简写，例如 `--yn-radius-sm/md/lg` 对应三个变量；CSS中使用其中一个完整名称。

## 使用规则

在功能自己的 CSS Module 中消费语义变量；共用颜色与尺寸变化应回到 design 层。颜色按文本、面板、危险等语义选取。示例：

```css
.panel {
  color: var(--yn-color-text);
  border: 1px solid var(--yn-color-border);
  border-radius: var(--yn-radius-md);
  background: var(--yn-color-surface);
  transition: border-color var(--yn-motion-fast) var(--yn-ease-standard);
}
```

日间与夜间共享变量名，主题值由根节点 `data-yinyu-theme` 与 design 样式控制。页面沿用这套主题入口。几何进度、画布坐标等运行值可用 CSS 自定义属性传递；普通视觉常量不放到 inline style。

新 vNext 页面不得依赖 Mantine 组件、旧 `yy-*` class 或 `YinyuTheme.css` / `YinyuRefinement.css` 全局覆盖。旧样式存在于仓库不代表可以重新引入。实际约束见 [前端架构检查器](../../../src/GZCTF/ClientApp/scripts/check-frontend-architecture.mjs)。

## 验证

在 390、1366、1920、2560 像素宽度检查重叠、页面横向滚动、布局跳动和主操作是否可达。表格或画布可以在自身容器滚动。支持键盘焦点、日夜主题与 `prefers-reduced-motion`；减少动效时保留状态信息，停止装饰性持续动画。

样式修改按 [开发边界](development.md) 运行前端门禁。路径与Token检查覆盖静态引用，视觉结果通过浏览器验证，并与源码测试分别记录。
