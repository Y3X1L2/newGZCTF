import { format } from 'echarts/core'

export function canvasLabelWidth(text: string, font = '600 13px "Inter Variable", sans-serif') {
  return Math.ceil(format.getTextRect(text, font).width)
}
