import DOMPurify, { Config } from 'dompurify'
import { Marked } from 'marked'
import { useMemo } from 'react'
import styles from './MarkdownContent.module.css'

const markdownSanitizeOptions: Config = {
  USE_PROFILES: { html: true },
  FORBID_TAGS: ['style', 'iframe', 'object', 'embed', 'form', 'input', 'button', 'meta', 'link', 'base'],
  FORBID_ATTR: ['style'],
  ALLOW_DATA_ATTR: false,
  ADD_ATTR: ['target'],
}

function slugify(value: string) {
  return value
    .trim()
    .toLocaleLowerCase('zh-CN')
    .replace(/[^\p{Letter}\p{Number}]+/gu, '-')
    .replace(/^-+|-+$/g, '')
}

export function sanitizeMarkdownHtml(html: string) {
  if (typeof document === 'undefined') return ''

  const template = document.createElement('template')
  template.innerHTML = DOMPurify.sanitize(html, markdownSanitizeOptions)
  template.content.querySelectorAll<HTMLAnchorElement>('a[target="_blank"]').forEach((element) => {
    element.setAttribute('rel', 'noreferrer noopener')
  })

  const headingCounts = new Map<string, number>()
  template.content.querySelectorAll<HTMLElement>('h2, h3').forEach((heading) => {
    const base = slugify(heading.textContent ?? '') || 'section'
    const count = headingCounts.get(base) ?? 0
    headingCounts.set(base, count + 1)
    heading.id = count ? `${base}-${count + 1}` : base
  })

  return template.innerHTML
}

export interface MarkdownOutlineItem {
  id: string
  label: string
  level: 2 | 3
}

export function markdownOutline(source: string): MarkdownOutlineItem[] {
  const counts = new Map<string, number>()
  return source
    .split('\n')
    .map((line) => line.match(/^(##|###)\s+(.+?)\s*#*$/))
    .filter((match): match is RegExpMatchArray => Boolean(match))
    .map((match) => {
      const label = match[2].replace(/[*_`[\]]/g, '').trim()
      const base = slugify(label) || 'section'
      const count = counts.get(base) ?? 0
      counts.set(base, count + 1)
      return {
        id: count ? `${base}-${count + 1}` : base,
        label,
        level: match[1] === '##' ? 2 : 3,
      } as MarkdownOutlineItem
    })
}

export function MarkdownContent({ source, className }: { source: string; className?: string }) {
  const html = useMemo(() => {
    const marked = new Marked({ breaks: true, gfm: true, silent: true })
    return sanitizeMarkdownHtml(String(marked.parse(source || '暂无内容。')))
  }, [source])

  return <div className={`${styles.markdown} ${className ?? ''}`} dangerouslySetInnerHTML={{ __html: html }} />
}
