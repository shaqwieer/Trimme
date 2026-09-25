# syntax=docker/dockerfile:1
# Build context: repository root (see infra/docker-compose.yml).

FROM node:22-alpine AS base
ENV PNPM_HOME=/pnpm \
    PATH=/pnpm:$PATH \
    NEXT_TELEMETRY_DISABLED=1
RUN corepack enable && corepack prepare pnpm@9.9.0 --activate

FROM base AS build
WORKDIR /repo
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml .npmrc ./
COPY apps/web/package.json apps/web/
COPY tests/E2E/package.json tests/E2E/
RUN pnpm install --frozen-lockfile --filter @trimme/web...
COPY apps/web/ apps/web/
# Destination of the /api and /hubs rewrites (fixed at build time; Nginx handles these paths in production).
ARG TRIMME_API_INTERNAL_URL=http://api:8080
ENV TRIMME_API_INTERNAL_URL=$TRIMME_API_INTERNAL_URL
RUN pnpm --filter @trimme/web build

FROM node:22-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
COPY --from=build --chown=node:node /repo/apps/web/.next/standalone ./
COPY --from=build --chown=node:node /repo/apps/web/.next/static ./apps/web/.next/static
COPY --from=build --chown=node:node /repo/apps/web/public ./apps/web/public
USER node
EXPOSE 3000
HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=3 \
  CMD ["node", "-e", "fetch('http://127.0.0.1:3000/ar').then(r => process.exit(r.ok ? 0 : 1)).catch(() => process.exit(1))"]
CMD ["node", "apps/web/server.js"]
