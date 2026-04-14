FROM node:24-slim

WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY package.json ./
RUN npm install --omit=dev
COPY src/ ./src/

ENV PORT=8083
EXPOSE 8083
CMD ["node", "src/index.js"]
