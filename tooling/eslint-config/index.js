import tseslint from '@typescript-eslint/eslint-plugin';
import tsparser from '@typescript-eslint/parser';
import prettier from 'eslint-config-prettier';

/**
 * Shared ESLint flat config for the Plan Cope monorepo.
 *
 * Designed to be imported by each package's `eslint.config.js` and re-exported.
 * Uses type-UNAWARE TypeScript parsing (no `project` option) so it works
 * without per-package tsconfig wiring. Packages that need type-aware rules
 * (e.g. recommended-type-checked) can extend this in their own config.
 */
export const sharedIgnores = ['node_modules/', 'dist/', 'build/', '.turbo/', 'coverage/', '*.config.*'];

export default [
  {
    ignores: sharedIgnores,
  },
  {
    files: ['**/*.ts', '**/*.tsx'],
    languageOptions: {
      parser: tsparser,
      parserOptions: {
        ecmaVersion: 2022,
        sourceType: 'module',
      },
    },
    plugins: {
      '@typescript-eslint': tseslint,
    },
    rules: {
      ...tseslint.configs.recommended.rules,
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
      '@typescript-eslint/explicit-function-return-type': 'off',
      '@typescript-eslint/no-explicit-any': 'warn',
      '@typescript-eslint/consistent-type-imports': 'error',
    },
  },
  prettier,
];
