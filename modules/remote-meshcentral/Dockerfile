FROM node:22-alpine
WORKDIR /opt/meshcentral
COPY modules/remote-meshcentral/package*.json ./
RUN npm ci --omit=dev
COPY modules/remote-meshcentral/entrypoint.mjs ./
USER node
EXPOSE 8081
ENTRYPOINT ["node", "entrypoint.mjs"]
