export function TokenResourceGrant({
  className,
  resourceId,
  resourceType,
  onResourceIdChange,
  onResourceTypeChange,
}: {
  className?: string
  resourceId: string
  resourceType: string
  onResourceIdChange: (value: string) => void
  onResourceTypeChange: (value: string) => void
}) {
  return (
    <div className={className}>
      <label>
        <span>资源类型</span>
        <input maxLength={64} onChange={(event) => onResourceTypeChange(event.currentTarget.value)} placeholder="game" value={resourceType} />
      </label>
      <label>
        <span>资源 ID</span>
        <input maxLength={128} onChange={(event) => onResourceIdChange(event.currentTarget.value)} placeholder="赛事 ID，例如 42" value={resourceId} />
      </label>
    </div>
  )
}

export function TokenResourceList({ resources }: { resources?: { resourceType?: string; resourceId?: string }[] | null }) {
  if (!resources?.length) return <small>未限制</small>
  return resources.map((resource) => (
    <span key={`${resource.resourceType}:${resource.resourceId}`}>
      {resource.resourceType}:{resource.resourceId}
    </span>
  ))
}
