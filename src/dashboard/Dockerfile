FROM node:22-alpine AS build
WORKDIR /build
COPY src/dashboard/package*.json ./
RUN npm ci
COPY src/dashboard/ ./
RUN npm run build
FROM caddy:2-alpine
COPY --from=build /build/dist /srv
COPY deploy/dashboard.Caddyfile /etc/caddy/Caddyfile
