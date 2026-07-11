import { generateColors } from '@mantine/colors-generator'
import {
  ActionIcon,
  Avatar,
  Badge,
  Code,
  Loader,
  MantineThemeOverride,
  Menu,
  Modal,
  Popover,
  Switch,
  Tabs,
  Tooltip,
  TooltipFloating,
} from '@mantine/core'
import tooltipClasses from '@Styles/Tooltip.module.css'

export const platformTheme: MantineThemeOverride = {
  colors: {
    gray: ['#EBEBEB', '#CFCFCF', '#B3B3B3', '#969696', '#7A7A7A', '#5E5E5E', '#414141', '#252525', '#202020', '#141414'],
    brand: ['#E1FFF9', '#CFFCF1', '#A2F7E2', '#72F1D2', '#4BEDC4', '#2AE5B5', '#18CB9E', '#00AA85', '#007F6E', '#005A4C'],
    alert: ['#FFB4B4', '#FFA0A0', '#FF8C8C', '#FF7878', '#FF6464', '#FE5050', '#FE3C3C', '#FE2828', '#FC1414', '#FC0000'],
    light: ['#FFFFFF', '#F8F8F8', '#EFEFEF', '#E0E0E0', '#DFDFDF', '#D0D0D0', '#CFCFCF', '#C0C0C0', '#BFBFBF', '#B0B0B0'],
    dark: ['#D5D7D7', '#ACAeAE', '#8C8F8F', '#666969', '#4D4F4F', '#343535', '#2B2C2C', '#1D1E1E', '#0C0D0D', '#010101'],
  },
  primaryColor: 'brand',
  defaultRadius: 'sm',
  fontFamily:
    'Lexend, -apple-system, BlinkMacSystemFont, Helvetica Neue, PingFang SC, Microsoft YaHei, Source Han Sans SC, Noto Sans CJK SC, sans-serif',
  fontFamilyMonospace: 'JetBrains Mono, ui-monospace, SFMono-Regular, Monaco, Consolas, Courier New, monospace, sans-serif',
  headings: {
    fontFamily:
      'Lexend, -apple-system, BlinkMacSystemFont, Helvetica Neue, PingFang SC, Microsoft YaHei, Source Han Sans SC, Noto Sans CJK SC, sans-serif',
    fontWeight: '850',
  },
  breakpoints: {
    xs: '30em',
    sm: '48em',
    md: '64em',
    lg: '74em',
    xl: '90em',
    w18: '1800px',
    w24: '2400px',
    w30: '3000px',
    w36: '3600px',
    w42: '4200px',
    w48: '4800px',
  },
  components: {
    Loader: Loader.extend({ defaultProps: { type: 'bars' } }),
    Switch: Switch.extend({
      styles: {
        body: { alignItems: 'center' },
        labelWrapper: { display: 'flex' },
      },
    }),
    Modal: Modal.extend({
      defaultProps: {
        centered: true,
        styles: { title: { fontWeight: 'bold' } },
      },
    }),
    Popover: Popover.extend({ defaultProps: { withinPortal: true } }),
    ActionIcon: ActionIcon.extend({ defaultProps: { variant: 'transparent' } }),
    Badge: Badge.extend({ defaultProps: { variant: 'outline' } }),
    Tabs: Tabs.extend({ styles: { tab: { padding: 'var(--mantine-spacing-xs)', fontWeight: 500 } } }),
    Avatar: Avatar.extend({ defaultProps: { color: 'brand' } }),
    Menu: Menu.extend({ styles: { item: { fontWeight: 500 } } }),
    Code: Code.extend({ styles: { root: { fontWeight: 500 } } }),
    Tooltip: Tooltip.extend({ classNames: tooltipClasses }),
    TooltipFloating: TooltipFloating.extend({ classNames: tooltipClasses }),
  },
}

export function createPlatformTheme(accent?: string | null): MantineThemeOverride {
  if (!accent) return platformTheme

  return {
    ...platformTheme,
    colors: {
      ...platformTheme.colors,
      custom: generateColors(accent),
    },
    components: {
      ...platformTheme.components,
      Avatar: Avatar.extend({ defaultProps: { color: 'custom' } }),
    },
    primaryColor: 'custom',
  }
}
