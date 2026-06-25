FROM node:22-alpine AS build

ARG NPM_VERSION=11.6.2

WORKDIR /app

RUN npm install -g "npm@${NPM_VERSION}"

COPY package*.json ./
RUN npm ci --legacy-peer-deps

COPY . .

ARG VITE_GOOGLE_CLIENT_ID
ARG VITE_APP_VERSION=0.1.0
ARG VITE_USE_CROSS_ORIGIN_APIS=false

ENV VITE_GOOGLE_CLIENT_ID=$VITE_GOOGLE_CLIENT_ID
ENV VITE_APP_VERSION=$VITE_APP_VERSION
ENV VITE_USE_CROSS_ORIGIN_APIS=$VITE_USE_CROSS_ORIGIN_APIS

RUN npm run build

FROM node:22-alpine AS runtime

ENV NODE_ENV=production
ENV PORT=8080

WORKDIR /app

COPY --from=build /app/dist ./dist
COPY server.js package.json ./

EXPOSE 8080

CMD ["node", "server.js"]
