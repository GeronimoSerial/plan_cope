import baseConfig from './index.js';
import reactHooks from 'eslint-plugin-react-hooks';
import jsxA11y from 'eslint-plugin-jsx-a11y';

export default [
  ...baseConfig,
  {
    files: ['**/*.tsx', '**/*.jsx'],
    plugins: {
      'react-hooks': reactHooks,
      'jsx-a11y': jsxA11y,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      ...jsxA11y.configs.recommended.rules,
    },
    settings: {
      react: { version: 'detect' },
    },
  },
];
