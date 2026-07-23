import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { HomeOrchestrationVisual } from './HomeOrchestrationVisual'

describe('HomeOrchestrationVisual', () => {
  it('renders as a decorative, non-interactive SVG', () => {
    render(<HomeOrchestrationVisual />)

    const visual = screen.getByTestId('home-orchestration-visual')
    expect(visual).toHaveAttribute('aria-hidden', 'true')
    expect(visual.querySelector('svg')).toHaveAttribute('viewBox', '0 0 760 420')
    expect(visual.querySelectorAll('a, button, input')).toHaveLength(0)
  })

  it('keeps SVG definition identifiers isolated across component instances', () => {
    const { container } = render(
      <>
        <HomeOrchestrationVisual />
        <HomeOrchestrationVisual />
      </>
    )

    const ids = [...container.querySelectorAll('defs [id]')].map((element) => element.id)
    expect(ids).toHaveLength(22)
    expect(new Set(ids).size).toBe(ids.length)

    for (const element of container.querySelectorAll(
      '[fill^="url(#"], [stroke^="url(#"], [clip-path^="url(#"], [mask^="url(#"]'
    )) {
      const reference =
        element.getAttribute('fill') ??
        element.getAttribute('stroke') ??
        element.getAttribute('clip-path') ??
        element.getAttribute('mask')
      const id = reference?.match(/^url\(#(.+)\)$/)?.[1]
      expect(id).toBeTruthy()
      expect(container.querySelector(`[id="${id}"]`)).not.toBeNull()
    }
  })
})
