/** @type {import('tailwindcss').Config} */
export default {
  darkMode: 'class',
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', 'sans-serif'],
      },
      colors: {
        ink: {
          DEFAULT: 'rgb(var(--ink) / <alpha-value>)',
          soft:    'rgb(var(--ink-soft) / <alpha-value>)',
          muted:   'rgb(var(--ink-muted) / <alpha-value>)',
        },
        paper: {
          DEFAULT: 'rgb(var(--paper) / <alpha-value>)',
          card:    'rgb(var(--paper-card) / <alpha-value>)',
          line:    'rgb(var(--paper-line) / <alpha-value>)',
        },
        accent: {
          pos:    'rgb(var(--accent-pos) / <alpha-value>)',
          neg:    'rgb(var(--accent-neg) / <alpha-value>)',
          invest: 'rgb(var(--accent-invest) / <alpha-value>)',
        },
      },
      boxShadow: {
        soft: '0 1px 2px rgba(0,0,0,0.06), 0 1px 1px rgba(0,0,0,0.04)',
      },
      borderRadius: {
        '4xl': '2rem',
        '5xl': '2.5rem',
      },
      transitionTimingFunction: {
        spring: 'cubic-bezier(0.34, 1.56, 0.64, 1)',
        ios: 'cubic-bezier(0.32, 0.72, 0, 1)',
      },
    },
  },
  plugins: [],
}
