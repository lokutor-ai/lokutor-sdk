# Development Guide

## Prerequisites

- [Node.js](https://nodejs.org/) (v18 or higher)
- [npm](https://www.npmjs.com/) or [yarn](https://yarnpkg.com/)

## Getting Started

1. Clone the repository
2. Install dependencies:
   ```bash
   npm install
   ```

## Building the SDK

The SDK is written in TypeScript and uses `tsup` for bundling into CJS, ESM, and type definitions.

```bash
npm run build
```

The output will be in the `dist/` directory.

## Testing

We use `vitest` for unit testing.

```bash
npm run test
```

## Linting

```bash
npm run lint
```

## Adding New Features

1. Define new types in `src/types.ts`.
2. Implement logic in `src/client.ts`.
3. Export new symbols in `src/index.ts`.
4. Add tests in `tests/`.
5. Update `API_REFERENCE.md`.
