import { describe, expect, it } from 'vitest'
import { markdownOutline, sanitizeMarkdownHtml } from './MarkdownContent'

describe('sanitizeMarkdownHtml', () => {
  it('removes executable HTML, SVG payloads and unsafe URLs', () => {
    const html = sanitizeMarkdownHtml(`
      <svg><a xlink:href="javascript:alert(1)"><circle /></a></svg>
      <img src="x" onerror="alert(1)" style="position:fixed">
      <a href="javascript:alert(1)" target="_blank">unsafe</a>
      <form><input name="token"></form>
    `)

    expect(html).not.toMatch(/<svg|onerror|style=|javascript:|<form|<input/i)
  })

  it('hardens external links and assigns stable unique heading ids', () => {
    const html = sanitizeMarkdownHtml(
      '<h2>重复标题</h2><h2>重复标题</h2><a href="https://example.test" target="_blank">link</a>'
    )

    expect(html).toContain('id="重复标题"')
    expect(html).toContain('id="重复标题-2"')
    expect(html).toContain('rel="noreferrer noopener"')
  })
})

describe('markdownOutline', () => {
  it('matches generated duplicate heading ids', () => {
    expect(markdownOutline('## 重复标题\n### 子节\n## 重复标题')).toEqual([
      { id: '重复标题', label: '重复标题', level: 2 },
      { id: '子节', label: '子节', level: 3 },
      { id: '重复标题-2', label: '重复标题', level: 2 },
    ])
  })
})
