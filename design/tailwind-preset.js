/** Tendero — Tailwind preset
 *  Shared by storefront and backoffice. Values point at the CSS custom
 *  properties in design/tokens.css so there is exactly one source of truth:
 *  change a token, both apps follow, dark mode included.
 *
 *  frontend/tailwind-preset.js
 */
module.exports = {
  theme: {
    extend: {
      colors: {
        clay: {
          50: 'var(--clay-50)',
          100: 'var(--clay-100)',
          500: 'var(--clay-500)',
          600: 'var(--clay-600)',
          800: 'var(--clay-800)'
        },
        olive: {
          50: 'var(--olive-50)',
          500: 'var(--olive-500)',
          700: 'var(--olive-700)'
        },
        stone: {
          0: 'var(--stone-0)',
          50: 'var(--stone-50)',
          100: 'var(--stone-100)',
          200: 'var(--stone-200)',
          300: 'var(--stone-300)'
        },
        ink: {
          400: 'var(--ink-400)',
          500: 'var(--ink-500)',
          700: 'var(--ink-700)',
          900: 'var(--ink-900)'
        },
        // Semantic — prefer these in components
        page: 'var(--bg-page)',
        surface: 'var(--bg-surface)',
        raised: 'var(--bg-raised)',
        hairline: 'var(--border)',
        accent: 'var(--accent)',
        'accent-hover': 'var(--accent-hover)',
        'accent-tint': 'var(--accent-tint)',
        positive: 'var(--positive)',
        warning: 'var(--warning)',
        danger: 'var(--danger)'
      },
      fontFamily: {
        display: 'var(--font-display)',
        sans: 'var(--font-body)',
        mono: 'var(--font-mono)'
      },
      fontSize: {
        '3xs': 'var(--text-3xs)',
        '2xs': 'var(--text-2xs)',
        xs: 'var(--text-xs)',
        sm: 'var(--text-sm)',
        base: 'var(--text-md)',
        lg: 'var(--text-lg)',
        xl: 'var(--text-xl)',
        '2xl': 'var(--text-2xl)',
        '3xl': 'var(--text-3xl)',
        '4xl': 'var(--text-4xl)'
      },
      borderRadius: {
        sm: 'var(--radius-sm)',
        DEFAULT: 'var(--radius-md)',
        lg: 'var(--radius-lg)',
        full: 'var(--radius-full)'
      },
      boxShadow: {
        pop: 'var(--shadow-pop)',
        none: 'none'
      },
      transitionTimingFunction: {
        DEFAULT: 'var(--ease)'
      },
      maxWidth: {
        prose: 'var(--container-prose)',
        app: 'var(--container-app)'
      }
    }
  },

  // Backoffice toggles dark via <html data-theme="dark">
  darkMode: ['selector', '[data-theme="dark"]'],

  plugins: [
    // The signature stripe as a utility: <hr class="awning">
    function ({ addUtilities }) {
      addUtilities({
        '.awning': {
          height: '6px',
          border: '0',
          borderRadius: 'var(--radius-full)',
          background:
            'repeating-linear-gradient(90deg, var(--clay-500) 0 14px, var(--clay-100) 14px 28px)'
        },
        '.awning-thin': { height: '3px' },
        '.numeric': {
          fontFamily: 'var(--font-mono)',
          fontVariantNumeric: 'tabular-nums',
          letterSpacing: '-0.01em'
        }
      });
    }
  ]
};
