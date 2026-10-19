FROM node:22-alpine
WORKDIR /opt/meshcentral
COPY modules/remote-meshcentral/package*.json ./
RUN npm ci --omit=dev
COPY modules/remote-meshcentral/patch-runtime.mjs ./
RUN node patch-runtime.mjs
COPY modules/remote-meshcentral/entrypoint.mjs ./
RUN mkdir -p /opt/meshcentral/meshcentral-backups && chown node:node /opt/meshcentral/meshcentral-backups
USER node
EXPOSE 8081
ENTRYPOINT ["node", "entrypoint.mjs"]
