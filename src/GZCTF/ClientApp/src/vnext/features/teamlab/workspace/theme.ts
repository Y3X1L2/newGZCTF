import { createTheme } from '@mantine/core'

export const workspaceTheme = createTheme({
  primaryColor: 'blue',
  colors: {
    blue: [
      '#f0f9ff',
      '#e0f2fe',
      '#b9e6fe',
      '#7cd4fd',
      '#36bffa',
      '#0ba5ec',
      '#0086c9',
      '#026aa2',
      '#065986',
      '#0b4a6f',
    ],
    dark: [
      '#f2f4f7',
      '#ced0d3',
      '#abafb5',
      '#7b8089',
      '#5c6066',
      '#484a4e',
      '#3a3b3f',
      '#2e2f33',
      '#222326',
      '#161719',
    ],
    red: ['#fffbfa', '#fef3f2', '#fee4e2', '#fecdca', '#fda29b', '#f97066', '#f04438', '#d92d20', '#b42318', '#912018'],
    green: [
      '#f6fef9',
      '#ecfdf3',
      '#d1fadf',
      '#a6f4c5',
      '#6ce9a6',
      '#32d583',
      '#12b76a',
      '#039855',
      '#027a48',
      '#05603a',
    ],
  },
  primaryShade: { light: 7, dark: 7 },
  defaultRadius: 'sm',
  radius: { xs: '4px', sm: '6px', md: '8px', lg: '12px', xl: '16px' },
  fontFamily: 'var(--font-ui)',
  fontFamilyMonospace: 'var(--font-mono)',
  fontSizes: { xs: '12px', sm: '14px', md: '14px', lg: '16px', xl: '18px' },
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
