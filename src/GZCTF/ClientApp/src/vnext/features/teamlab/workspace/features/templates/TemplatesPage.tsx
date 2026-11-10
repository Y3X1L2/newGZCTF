import { TextInput } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { Box, Boxes, Monitor, Search } from 'lucide-react'
import { useState } from 'react'
import { useWorkspaceApi } from '../../api/client'
import { Empty, ErrorMessage, Loading } from '../../foundation/Feedback'
import { LoadMore } from '../../foundation/LoadMore'
import { Status } from '../../foundation/Status'
import { memory } from '../../foundation/format'
import { useCursorList } from '../../foundation/useCursorList'
import { useSearchParams } from './../../routing'
import { BlueprintsPanel } from './BlueprintsPanel'
import { TemplateDetails } from './TemplateDetails'
import styles from './Templates.module.css'

export function TemplatesPage() {
  const [params, setParams] = useSearchParams()
  const section = params.get('tab') === 'environments' ? 'environments' : 'assets'
  return (
    <main className="collection-page">
      <div className="page-heading">
        <h1>模板</h1>
        <div className="module-switch" role="group" aria-label="模板分类">
          {[
            ['assets', '资产模板'],
            ['environments', '环境模板'],
          ].map(([value, label]) => (
            <button
              key={value}
              aria-pressed={section === value}
              onClick={() => setParams(value === 'assets' ? {} : { tab: value })}
            >
              {label}
            </button>
          ))}
        </div>
      </div>
      {section === 'assets' ? <AssetTemplatesPanel /> : <BlueprintsPanel />}
    </main>
  )
}

function AssetTemplatesPanel() {
  const api = useWorkspaceApi()
  const [kind, setKind] = useState('all')
  const [query, setQuery] = useState('')
  const [search] = useDebouncedValue(query, 250)
  const selectedKind = kind === 'all' ? '' : kind
  const templates = useCursorList(
    ['templates', { search, kind: selectedKind }],
    (page) => api.templates({ ...page, search, kind: selectedKind }),
    {
      refetchInterval: (items) =>
        items.some((item) => item.state === 'importing' || (item.state === 'deleting' && !item.error)) ? 2500 : false,
    }
  )
  const [selected, setSelected] = useState<string>()
  const items = templates.data ?? []
  const detail = items.find((item) => item.id === selected) ?? items[0]
  return (
    <>
      <section className={styles.catalog} aria-label="资产模板库">
        <div className={styles.catalogTools}>
          <TextInput
            aria-label="搜索模板"
            placeholder="搜索模板"
            leftSection={<Search size={15} />}
            value={query}
            onChange={(event) => setQuery(event.currentTarget.value)}
          />
          <div className="filter-pills" role="group" aria-label="模板类型">
            {[
              ['all', '全部模板'],
              ['vm', '虚拟机'],
              ['container', '容器'],
            ].map(([value, label]) => (
              <button
                key={value}
                className={kind === value ? 'selected' : ''}
                onClick={() => setKind(value)}
                aria-pressed={kind === value}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
        <ErrorMessage error={templates.error} />
        {templates.isPending ? (
          <Loading />
        ) : items.length || query || kind !== 'all' ? (
          <div className={styles.library}>
            <aside className={styles.directory} aria-label="资产模板目录">
              <div className={styles.items}>
                {items.map((template) => (
                  <button
                    key={template.id}
                    className={styles.item}
                    aria-label={template.name}
                    aria-pressed={template.id === detail?.id}
                    onClick={() => setSelected(template.id)}
                  >
                    <span className={styles.symbol}>
                      {template.kind === 'vm' ? <Monitor size={20} /> : <Box size={20} />}
                    </span>
                    <span className={styles.itemText}>
                      <strong>{template.name}</strong>
                      <span>
                        {template.os} ·{' '}
                        {template.format?.toUpperCase() ?? (template.kind === 'container' ? 'OCI' : '—')} · v
                        {template.version}
                      </span>
                      <span>
                        {template.resources.cpu} 核 · {memory(template.resources.memoryMiB)}
                      </span>
                    </span>
                    <Status value={template.state ?? 'importing'} />
                  </button>
                ))}
                {!items.length && <Empty icon={<Search size={24} />} title="没有匹配的模板" />}
                <LoadMore list={templates} />
              </div>
            </aside>
            {detail ? <TemplateDetails key={detail.id} template={detail} /> : <div />}
          </div>
        ) : (
          !templates.error && (
            <Empty icon={<Boxes size={30} />} title={kind !== 'all' ? '没有匹配的模板' : '暂无资产模板'} />
          )
        )}
      </section>
    </>
  )
}
