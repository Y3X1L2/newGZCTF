import { createTheme, defaultVariantColorsResolver } from '@mantine/core'

export const workspaceTheme = createTheme({
  primaryColor: 'yinyu',
  colors: {
    yinyu: [
      'var(--accent-soft)',
      'var(--accent-soft)',
      'var(--accent-soft)',
      'var(--accent-soft)',
      'var(--accent)',
      'var(--accent)',
      'var(--accent)',
      'var(--accent)',
      'var(--accent)',
      'var(--accent)',
    ],
  },
  variantColorResolver: (input) => {
    const resolved = defaultVariantColorsResolver(input)
    if (input.color && input.color !== 'yinyu') return resolved
    if (input.variant === 'filled')
      return {
        background: 'var(--yn-color-control-strong)',
        hover: 'var(--yn-color-control-strong-hover)',
        color: 'var(--yn-color-control-strong-text)',
        border: 'transparent',
      }
    if (input.variant === 'light')
      return {
        background: 'var(--accent-soft)',
        hover: 'var(--accent-soft)',
        color: 'var(--accent)',
        border: 'transparent',
      }
    if (input.variant === 'outline' || input.variant === 'subtle')
      return {
        background: 'transparent',
        hover: 'var(--yn-color-surface-muted)',
        color: 'var(--yn-color-text-secondary)',
        border: input.variant === 'outline' ? '1px solid var(--yn-color-border)' : 'transparent',
      }
    return resolved
  },
  primaryShade: { light: 7, dark: 7 },
  defaultRadius: 'sm',
  radius: {
    xs: 'var(--yn-radius-sm)',
    sm: 'var(--yn-radius-md)',
    md: 'var(--yn-radius-lg)',
    lg: 'var(--yn-radius-lg)',
    xl: 'var(--yn-radius-lg)',
  },
  fontFamily: 'var(--font-ui)',
  fontFamilyMonospace: 'var(--font-mono)',
  fontSizes: {
    xs: 'var(--yn-font-sm)',
    sm: 'var(--yn-font-md)',
    md: 'var(--yn-font-md)',
    lg: 'var(--yn-font-lg)',
    xl: 'var(--yn-font-xl)',
  },
  headings: { fontWeight: '600' },
  components: {
    Button: {
      defaultProps: { size: 'sm' },
      styles: { root: { fontWeight: 500, letterSpacing: 0 } },
    },
    TextInput: { defaultProps: { size: 'sm' } },
    Select: { defaultProps: { size: 'sm' } },
    PasswordInput: { defaultProps: { size: 'sm' } },
    NumberInput: { defaultProps: { size: 'sm' } },
    MultiSelect: { defaultProps: { size: 'sm' } },
    InputWrapper: {
      styles: {
        label: {
          fontSize: 12,
          fontWeight: 500,
          color: 'var(--muted)',
          marginBottom: 6,
        },
      },
    },
    Drawer: {
      styles: {
        content: { background: 'var(--surface)' },
        header: {
          background: 'var(--surface)',
          borderBottom: '1px solid var(--line)',
          minHeight: 58,
        },
        title: { fontSize: 16, fontWeight: 600 },
        body: { paddingTop: 24 },
      },
    },
    Modal: {
      defaultProps: { radius: 'md' },
      styles: {
        content: { background: 'var(--surface)' },
        header: { background: 'var(--surface)' },
        title: { fontSize: 16, fontWeight: 600 },
      },
    },
    Menu: {
      defaultProps: { shadow: 'sm', offset: 6 },
      styles: {
        dropdown: { background: 'var(--surface)', borderColor: 'var(--line)' },
        item: { fontSize: 14 },
      },
    },
  },
})
