import type { EChartsOption } from 'echarts'
import * as echarts from 'echarts'
import { FC, useEffect, useRef } from 'react'

export interface EchartsContainerProps extends React.ComponentPropsWithoutRef<'div'> {
  option: EChartsOption
  opts?: echarts.EChartsInitOpts
  style?: React.CSSProperties
}

export const EchartsContainer: FC<EchartsContainerProps> = (props) => {
  const chartRef = useRef<HTMLDivElement>(null)
  const chartInstance = useRef<echarts.ECharts | null>(null)
  const optionRef = useRef<EChartsOption>(props.option)
  const applyFrameRef = useRef(0)
  const revealFrameRef = useRef(0)
  const readyRef = useRef(false)
  const { option, opts, style, ...rest } = props
  optionRef.current = option

  const scheduleApplyOption = () => {
    if (applyFrameRef.current) {
      cancelAnimationFrame(applyFrameRef.current)
    }

    applyFrameRef.current = requestAnimationFrame(() => {
      applyFrameRef.current = 0
      const chart = chartInstance.current
      const el = chartRef.current

      if (!chart || !el) return

      chart.resize()
      chart.setOption(optionRef.current, { notMerge: true, lazyUpdate: false })

      if (!readyRef.current) {
        if (revealFrameRef.current) cancelAnimationFrame(revealFrameRef.current)

        revealFrameRef.current = requestAnimationFrame(() => {
          revealFrameRef.current = 0
          chart.resize()
          el.style.visibility = 'visible'
          readyRef.current = true
        })
      }
    })
  }

  useEffect(() => {
    if (chartRef.current && !chartInstance.current) {
      chartRef.current.style.visibility = 'hidden'
      chartInstance.current = echarts.init(chartRef.current, 'dark', opts)
      scheduleApplyOption()
    }

    return () => {
      if (applyFrameRef.current) {
        cancelAnimationFrame(applyFrameRef.current)
        applyFrameRef.current = 0
      }
      if (revealFrameRef.current) {
        cancelAnimationFrame(revealFrameRef.current)
        revealFrameRef.current = 0
      }
      if (chartInstance.current) {
        chartInstance.current.dispose()
        chartInstance.current = null
      }
    }
  }, [])

  useEffect(() => {
    if (chartInstance.current) {
      scheduleApplyOption()
    }
  }, [option])

  useEffect(() => {
    const el = chartRef.current
    if (!el) return undefined

    const findScrollTarget = () => {
      let current = el.parentElement

      while (current) {
        const { overflowY } = window.getComputedStyle(current)
        const canScroll = /(auto|scroll|overlay)/.test(overflowY) && current.scrollHeight > current.clientHeight

        if (canScroll) return current

        current = current.parentElement
      }

      return document.scrollingElement
    }

    const handleWheel = (event: WheelEvent) => {
      if (event.ctrlKey || Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return

      const target = findScrollTarget()
      if (!target) return

      event.preventDefault()
      event.stopImmediatePropagation()
      target.scrollTop += event.deltaY
    }

    el.addEventListener('wheel', handleWheel, { capture: true, passive: false })

    return () => {
      el.removeEventListener('wheel', handleWheel, { capture: true })
    }
  }, [])

  useEffect(() => {
    const el = chartRef.current
    if (!el) return undefined

    let frame = 0
    const handleResize = () => {
      if (frame) return
      frame = requestAnimationFrame(() => {
        frame = 0
        chartInstance.current?.resize()
      })
    }

    const observer = new ResizeObserver(handleResize)
    observer.observe(el)
    window.addEventListener('resize', handleResize)

    return () => {
      if (frame) cancelAnimationFrame(frame)
      observer.disconnect()
      window.removeEventListener('resize', handleResize)
    }
  }, [])

  return <div ref={chartRef} style={style} {...rest} />
}
