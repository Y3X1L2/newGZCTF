import { useMemo, useState } from 'react'
import type { UserSkillDimension } from './api/userProfileApi'
import {
  orderedDimensions,
  profileDimensionLabel,
  radarGrid,
  radarLabelPoint,
  radarPolygon,
} from './profileDomain'
import { dimensionDefinition } from './skillDimensionRegistry'
import styles from './UserProfilePage.module.css'

export function ProfileSkillMap({ dimensions }: { dimensions: UserSkillDimension[] }) {
  const ordered = useMemo(() => orderedDimensions(dimensions), [dimensions])
  const [activeId, setActiveId] = useState<string | null>(null)
  const polygon = radarPolygon(ordered)

  return (
    <section className={styles.profilePanel}>
      <header className={styles.panelHeading}>
        <div>
          <span className={styles.panelEyebrow}>CHALLENGE PROFILE</span>
          <h2>分类解题画像</h2>
        </div>
        <span>按平台 P90 基准归一化</span>
      </header>
      <div className={styles.skillLayout}>
        <div className={styles.radarWrap}>
          <svg aria-label="分类解题雷达图" className={styles.radar} role="img" viewBox="0 0 240 240">
            {[22, 43, 64, 86].map((radius) => (
              <polygon className={styles.radarGrid} key={radius} points={radarGrid(ordered.length, radius)} />
            ))}
            {ordered.map((dimension, index) => {
              const label = radarLabelPoint(index, ordered.length)
              const axis = radarLabelPoint(index, ordered.length)
              return (
                <g data-active={activeId === dimension.id || undefined} key={dimension.id}>
                  <line className={styles.radarAxis} x1="120" x2={axis.x} y1="120" y2={axis.y} />
                  <text className={styles.radarLabel} textAnchor="middle" x={label.x} y={label.y + 4}>
                    {profileDimensionLabel(dimension.id)}
                  </text>
                </g>
              )
            })}
            <polygon className={styles.radarValue} points={polygon} />
          </svg>
        </div>
        <div className={styles.dimensionTable} role="table" aria-label="分类真实统计">
          <div className={styles.dimensionHeader} role="row">
            <span role="columnheader">方向</span>
            <span role="columnheader">解题</span>
            <span role="columnheader">尝试</span>
            <span role="columnheader">提交</span>
            <span role="columnheader">正确率</span>
          </div>
          {ordered.map((dimension) => (
            <div
              className={styles.dimensionRow}
              data-active={activeId === dimension.id || undefined}
              key={dimension.id}
              onMouseEnter={() => setActiveId(dimension.id)}
              onMouseLeave={() => setActiveId(null)}
              role="row"
            >
              <button
                onBlur={() => setActiveId(null)}
                onFocus={() => setActiveId(dimension.id)}
                title={dimensionDefinition(dimension.id).description}
                type="button"
              >
                <strong>{profileDimensionLabel(dimension.id)}</strong>
                {!dimension.sampleSufficient ? <small>样本不足</small> : null}
                <progress aria-label={`${profileDimensionLabel(dimension.id)}画像值`} max="100" value={dimension.radarValue} />
              </button>
              <span role="cell">{dimension.solved}</span>
              <span role="cell">{dimension.attempted}</span>
              <span role="cell">{dimension.submissions}</span>
              <span role="cell">{dimension.successRate}%</span>
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}
