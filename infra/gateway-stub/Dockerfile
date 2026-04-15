FROM node:24-slim

WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY package.json ./
COPY index.js ./

ENV PORT=9999
ENV STUB_FAILURE_RATE=0.1

EXPOSE 9999
CMD ["node", "index.js"]
