import baseConfig from '@plan-cope/eslint-config';

export default [
  ...baseConfig,
  {
    ignores: ['dist/', 'node_modules/', 'coverage/'],
  },
];
