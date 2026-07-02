import { useEffect } from 'react'

const BENTO_SELECTOR = [
  '.panel-card',
  '.yy-panel-card',
  '.game-index-card',
  '.recent-game-card',
  '.post-preview',
  '.admin-panel',
  '.metric-tile',
  '.state-card',
  '.challenge-card',
  '.challenge-drawer-draft',
  '.yy-table-shell',
  '.yy-form-section',
  '.admin-tab-card',
  '.yy-about-hive-cell',
  '.yy-home-panel-heading',
  '.yy-home-notice-rail',
  '.yy-home-event-board',
  '.yy-status-pill',
  '.yy-team-switch-card',
  '.yy-team-roster-row',
  '.yy-team-member-summary > div',
  '.yy-game-time-grid > div',
  '.yy-notice-filter-button',
  '.mantine-Badge-root',
  '.mantine-Button-root',
  '.mantine-ActionIcon-root',
  '.mantine-Switch-track',
  '.mantine-Table-tr',
].join(',')

const AMBIENT_SELECTOR = [
  '.yy-react-gradient-text',
  '.yy-game-bends-bg',
  '.yy-signal-field',
  '.yy-color-bends-field',
  '.yy-brand-title',
  '.yy-home-brand-heading',
  '.yy-section-head',
  '.yy-section-kicker',
  '.yy-home-title-row',
].join(',')

const BENTO_CLASS = 'yy-bento-tone-active'
const AMBIENT_CLASS = 'yy-bento-ambient-active'
const NEAR_CLASS = 'yy-bento-tone-near'

const clamp = (value: number, min = 0, max = 1) => Math.min(max, Math.max(min, value))

const smoother = (value: number) => {
  const t = clamp(value)
  return t * t * (3 - 2 * t)
}

const effectiveDistance = (x: number, y: number, rect: DOMRect) => {
  const dx = Math.max(rect.left - x, 0, x - rect.right)
  const dy = Math.max(rect.top - y, 0, y - rect.bottom)
  return Math.hypot(dx, dy)
}

const setBentoVars = (element: HTMLElement, x: number, y: number, intensity: number, radius: number) => {
  const rect = element.getBoundingClientRect()
  const localX = clamp((x - rect.left) / Math.max(rect.width, 1), 0, 1) * 100
  const localY = clamp((y - rect.top) / Math.max(rect.height, 1), 0, 1) * 100
  const centerX = rect.left + rect.width / 2
  const centerY = rect.top + rect.height / 2
  const angle = Math.atan2(y - centerY, x - centerX) * (180 / Math.PI)
  const noise = Math.sin((x + rect.left) * 0.013 + (y + rect.top) * 0.017) * 0.5 + 0.5
  const warped = smoother(intensity)

  element.style.setProperty('--glow-x', `${localX.toFixed(2)}%`)
  element.style.setProperty('--glow-y', `${localY.toFixed(2)}%`)
  element.style.setProperty('--glow-intensity', warped.toFixed(3))
  element.style.setProperty('--glow-radius', `${Math.round(radius)}px`)
  element.style.setProperty('--yy-bento-x', `${localX.toFixed(2)}%`)
  element.style.setProperty('--yy-bento-y', `${localY.toFixed(2)}%`)
  element.style.setProperty('--yy-bento-intensity', warped.toFixed(3))
  element.style.setProperty('--yy-bento-angle', `${angle.toFixed(1)}deg`)
  element.style.setProperty('--yy-bento-noise', noise.toFixed(3))
}

const clearVars = (element: HTMLElement) => {
  element.classList.remove(BENTO_CLASS, AMBIENT_CLASS, NEAR_CLASS)
  element.style.removeProperty('--glow-x')
  element.style.removeProperty('--glow-y')
  element.style.removeProperty('--glow-intensity')
  element.style.removeProperty('--glow-radius')
  element.style.removeProperty('--yy-bento-x')
  element.style.removeProperty('--yy-bento-y')
  element.style.removeProperty('--yy-bento-intensity')
  element.style.removeProperty('--yy-bento-angle')
  element.style.removeProperty('--yy-bento-noise')
}

export function YinyuPointerGlow() {
  useEffect(() => {
    if (window.matchMedia('(pointer: coarse)').matches) return undefined

    let frame = 0
    let hasPointer = false
    let activeElements = new Set<HTMLElement>()
    let ambientElements = new Set<HTMLElement>()
    let candidates: HTMLElement[] = []
    let ambientCandidates: HTMLElement[] = []
    let lastScan = 0
    let rescanFrame = 0
    const pointer = { x: window.innerWidth * 0.5, y: window.innerHeight * 0.42 }

    const rescan = () => {
      candidates = Array.from(document.querySelectorAll<HTMLElement>(BENTO_SELECTOR)).filter((element) => {
        if (!element.isConnected) return false
        if (element.closest('[aria-hidden="true"]')) return false
        return true
      })
      ambientCandidates = Array.from(document.querySelectorAll<HTMLElement>(AMBIENT_SELECTOR)).filter((element) => element.isConnected)
      lastScan = performance.now()
    }

    const update = () => {
      frame = 0

      if (performance.now() - lastScan > 900) {
        rescan()
      }

      const nextActive = new Set<HTMLElement>()
      const nextAmbient = new Set<HTMLElement>()

      if (hasPointer) {
        const viewportWidth = window.innerWidth
        const viewportHeight = window.innerHeight

        candidates.forEach((element) => {
          const rect = element.getBoundingClientRect()
          if (rect.width <= 0 || rect.height <= 0) return
          if (rect.bottom < -120 || rect.top > viewportHeight + 120 || rect.right < -120 || rect.left > viewportWidth + 120) return

          const inside =
            pointer.x >= rect.left && pointer.x <= rect.right && pointer.y >= rect.top && pointer.y <= rect.bottom
          const proximity = Math.min(460, Math.max(190, Math.max(rect.width, rect.height) * 0.58 + 140))
          const fadeDistance = proximity * 1.86
          const distance = effectiveDistance(pointer.x, pointer.y, rect)
          let intensity = 0

          if (inside) intensity = 1
          else if (distance <= proximity) intensity = 0.88
          else if (distance <= fadeDistance) intensity = (fadeDistance - distance) / (fadeDistance - proximity) * 0.88

          if (intensity <= 0.03) return

          element.classList.add(BENTO_CLASS)
          if (!inside) element.classList.add(NEAR_CLASS)
          else element.classList.remove(NEAR_CLASS)

          setBentoVars(element, pointer.x, pointer.y, intensity, fadeDistance)
          nextActive.add(element)
        })

        ambientCandidates.forEach((element) => {
          const rect = element.getBoundingClientRect()
          if (rect.width <= 0 || rect.height <= 0) return
          if (rect.bottom < -160 || rect.top > viewportHeight + 160 || rect.right < -160 || rect.left > viewportWidth + 160) return

          const distance = effectiveDistance(pointer.x, pointer.y, rect)
          const radius = Math.min(760, Math.max(320, Math.max(rect.width, rect.height) * 0.5 + 260))
          const intensity = distance <= radius ? smoother(1 - distance / radius) : 0
          if (intensity <= 0.025) return

          element.classList.add(AMBIENT_CLASS)
          setBentoVars(element, pointer.x, pointer.y, intensity, radius)
          nextAmbient.add(element)
        })
      }

      activeElements.forEach((element) => {
        if (!nextActive.has(element) && !nextAmbient.has(element)) clearVars(element)
      })
      ambientElements.forEach((element) => {
        if (!nextAmbient.has(element) && !nextActive.has(element)) clearVars(element)
      })
      activeElements = nextActive
      ambientElements = nextAmbient
    }

    const schedule = () => {
      if (frame) return
      frame = window.requestAnimationFrame(update)
    }

    const handlePointerMove = (event: PointerEvent) => {
      if (event.pointerType === 'touch') return
      pointer.x = event.clientX
      pointer.y = event.clientY
      hasPointer = true
      schedule()
    }

    const handlePointerLeave = () => {
      hasPointer = false
      schedule()
    }

    const handleRescan = () => {
      rescan()
      schedule()
    }

    const scheduleRescan = () => {
      if (rescanFrame) return
      rescanFrame = window.requestAnimationFrame(() => {
        rescanFrame = 0
        handleRescan()
      })
    }

    rescan()
    const observer = new MutationObserver(scheduleRescan)
    observer.observe(document.body, { childList: true, subtree: true })

    window.addEventListener('pointermove', handlePointerMove, { passive: true })
    window.addEventListener('pointerleave', handlePointerLeave)
    window.addEventListener('blur', handlePointerLeave)
    window.addEventListener('scroll', schedule, { passive: true })
    window.addEventListener('resize', scheduleRescan)

    return () => {
      if (frame) window.cancelAnimationFrame(frame)
      if (rescanFrame) window.cancelAnimationFrame(rescanFrame)
      observer.disconnect()
      window.removeEventListener('pointermove', handlePointerMove)
      window.removeEventListener('pointerleave', handlePointerLeave)
      window.removeEventListener('blur', handlePointerLeave)
      window.removeEventListener('scroll', schedule)
      window.removeEventListener('resize', scheduleRescan)
      activeElements.forEach(clearVars)
      ambientElements.forEach(clearVars)
      activeElements.clear()
      ambientElements.clear()
    }
  }, [])

  return null
}
