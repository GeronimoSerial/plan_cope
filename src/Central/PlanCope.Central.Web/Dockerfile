FROM node:24-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
COPY src/Central/PlanCope.Central.Web/package.json src/Central/PlanCope.Central.Web/package.json
COPY src/Local/PlanCope.Local.Host/ClientApp/package.json src/Local/PlanCope.Local.Host/ClientApp/package.json
RUN npm ci

FROM deps AS build
COPY src/Central/PlanCope.Central.Web/ src/Central/PlanCope.Central.Web/
RUN npm run build --workspace plancope-central-web

FROM node:24-alpine AS runtime
ARG SOURCE="https://github.com/geronimoserial/plan-cope"
ARG REVISION="unknown"
ARG VERSION="0.0.0-local"
LABEL org.opencontainers.image.source="${SOURCE}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.version="${VERSION}"
WORKDIR /app
ENV NODE_ENV=production \
    HOSTNAME=0.0.0.0 \
    PORT=3000
COPY --from=build --chown=node:node /app/src/Central/PlanCope.Central.Web/.next/standalone ./
COPY --from=build --chown=node:node /app/src/Central/PlanCope.Central.Web/.next/static ./src/Central/PlanCope.Central.Web/.next/static
USER node
EXPOSE 3000
CMD ["node", "src/Central/PlanCope.Central.Web/server.js"]
